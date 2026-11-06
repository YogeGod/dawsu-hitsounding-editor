// dawsu! isolated VST3 instrument host.
//
// One process hosts one VST3 instrument for one Keysound track, so a crashing plug-in only takes down its own track.
// Built directly on the Steinberg VST 3 SDK (MIT licence since 3.8); no JUCE.
//
//   dawsu_vst3_host --scan <plugin.vst3>
//       Prints one line per instrument class: CLASS \t <uid> \t <name> \t <vendor> \t <subcategories>; exits 0.
//   dawsu_vst3_host --host <name> <parentPid> <plugin.vst3> [<uid>]
//       Loads the instrument and prints READY \t <name> \t <protocol> (or ERROR \t <message>) on stdout. Then:
//       - Audio: the shared memory "Local\<name>" (layout below). dawsu! fills events and frames, bumps requestSeq
//         and signals "Local\<name>-req"; the host renders, sets doneSeq and signals "Local\<name>-done".
//       - Control: "<id> \t <command>" per stdin line, "<id> \t <reply>" per stdout line:
//         SHOW / HIDE (the plug-in's editor window), GETSTATE -> STATE \t <base64>, SETSTATE \t <base64> -> OK, QUIT.
//         Errors reply ERROR \t <message>. The state is a format byte (1), then component and controller state.
//       The host exits when dawsu! (parentPid) exits.

#include "public.sdk/source/vst/hosting/module.h"
#include "public.sdk/source/vst/hosting/plugprovider.h"
#include "public.sdk/source/vst/hosting/hostclasses.h"
#include "public.sdk/source/vst/hosting/eventlist.h"
#include "public.sdk/source/vst/hosting/parameterchanges.h"
#include "public.sdk/source/vst/hosting/processdata.h"
#include "public.sdk/source/common/memorystream.h"
#include "pluginterfaces/gui/iplugview.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "pluginterfaces/vst/ivstmidicontrollers.h"
#include "pluginterfaces/vst/ivstprocesscontext.h"

#define NOMINMAX
#include <windows.h>
#include <wincrypt.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <iostream>
#include <map>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

using namespace Steinberg;
using namespace Steinberg::Vst;

namespace {

// ---- Shared memory: mirrored by OsuHitsound.Audio.IsolatedVst3 (keep the two in step) ----
constexpr uint32_t Magic = 0x31485644; // "DVH1"
constexpr int MaxEvents = 1024, MaxFrames = 4096;
struct ShmEvent { int32_t offset; uint8_t status, data1, data2, pad; };
struct Shm
{
    uint32_t magic, version;
    int32_t frames, eventCount;
    uint32_t requestSeq, doneSeq;
    int32_t status, reserved;    // status: 0 rendered, 1 not rendered (silence)
    double sampleRate;
    ShmEvent events[MaxEvents];
    float left[MaxFrames], right[MaxFrames];
};
static_assert(sizeof(ShmEvent) == 8, "event layout");
static_assert(offsetof(Shm, events) == 40 && offsetof(Shm, left) == 40 + MaxEvents * 8, "shared memory layout");

// Replies go to the real stdout handle only. The process's stdout is pointed at NUL so a plug-in that prints (some do)
// can't corrupt the protocol.
constexpr int Protocol = 2; // 2: --offline and RENDER
constexpr size_t MaxStateBytes = 64u * 1024 * 1024;
std::mutex outLock;
HANDLE protocolOut = INVALID_HANDLE_VALUE;
void Reply(const std::string& line)
{
    std::lock_guard<std::mutex> lock(outLock);
    std::string text = line + "\n";
    DWORD written = 0;
    for (size_t at = 0; at < text.size(); at += written)
        if (!WriteFile(protocolOut, text.data() + at, (DWORD)(text.size() - at), &written, nullptr) || written == 0) return;
}

void IsolateStdout()
{
    // A duplicate: reopening stdout below closes the original handle.
    DuplicateHandle(GetCurrentProcess(), GetStdHandle(STD_OUTPUT_HANDLE), GetCurrentProcess(), &protocolOut, 0, FALSE, DUPLICATE_SAME_ACCESS);
    HANDLE nul = CreateFileW(L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    SetStdHandle(STD_OUTPUT_HANDLE, nul);
    FILE* ignored = nullptr;
    freopen_s(&ignored, "NUL", "w", stdout);
}

std::string Clean(std::string s)
{
    for (auto& c : s) if (c == '\t' || c == '\n' || c == '\r') c = ' ';
    return s;
}

// ---- Base64 for state blobs on the text control channel (Windows' crypt32) ----
std::string ToBase64(const std::vector<uint8_t>& in)
{
    if (in.empty()) return "";
    DWORD n = 0;
    const DWORD flags = CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF;
    if (!CryptBinaryToStringA(in.data(), (DWORD)in.size(), flags, nullptr, &n)) return "";
    std::string out(n, '\0');
    if (!CryptBinaryToStringA(in.data(), (DWORD)in.size(), flags, out.data(), &n)) return "";
    out.resize(n);
    return out;
}

bool FromBase64(const std::string& in, size_t from, std::vector<uint8_t>& out)
{
    DWORD n = 0;
    const char* text = in.data() + from;
    DWORD length = (DWORD)(in.size() - from);
    if (length == 0) { out.clear(); return true; }
    if (!CryptStringToBinaryA(text, length, CRYPT_STRING_BASE64, nullptr, &n, nullptr, nullptr) || n > MaxStateBytes) return false;
    out.resize(n);
    return CryptStringToBinaryA(text, length, CRYPT_STRING_BASE64, out.data(), &n, nullptr, nullptr) && (out.resize(n), true);
}

// ---- Host-side interfaces the plug-in calls ----

// Parameter edits made in the plug-in's editor reach its processor through the next block's parameter changes.
class ComponentHandler : public IComponentHandler
{
public:
    std::mutex lock;
    std::vector<std::pair<ParamID, ParamValue>> pending;

    tresult PLUGIN_API beginEdit(ParamID) override { return kResultOk; }
    tresult PLUGIN_API performEdit(ParamID id, ParamValue value) override
    {
        std::lock_guard<std::mutex> g(lock);
        if (pending.size() < 4096) pending.emplace_back(id, value);
        return kResultOk;
    }
    tresult PLUGIN_API endEdit(ParamID) override { return kResultOk; }
    tresult PLUGIN_API restartComponent(int32) override { return kResultOk; }
    tresult PLUGIN_API queryInterface(const TUID iid, void** obj) override
    {
        if (FUnknownPrivate::iidEqual(iid, IComponentHandler::iid) || FUnknownPrivate::iidEqual(iid, FUnknown::iid))
        {
            *obj = this;
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }
    uint32 PLUGIN_API addRef() override { return 1; }
    uint32 PLUGIN_API release() override { return 1; }
};

HWND editorWindow = nullptr;
IPtr<IPlugView> view;

class PlugFrame : public IPlugFrame
{
public:
    tresult PLUGIN_API resizeView(IPlugView* v, ViewRect* r) override
    {
        if (!editorWindow || !r) return kInvalidArgument;
        RECT rect{0, 0, r->getWidth(), r->getHeight()};
        AdjustWindowRect(&rect, GetWindowLong(editorWindow, GWL_STYLE), FALSE);
        SetWindowPos(editorWindow, nullptr, 0, 0, rect.right - rect.left, rect.bottom - rect.top, SWP_NOMOVE | SWP_NOZORDER);
        return v ? v->onSize(r) : kResultOk;
    }
    tresult PLUGIN_API queryInterface(const TUID iid, void** obj) override
    {
        if (FUnknownPrivate::iidEqual(iid, IPlugFrame::iid) || FUnknownPrivate::iidEqual(iid, FUnknown::iid))
        {
            *obj = this;
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }
    uint32 PLUGIN_API addRef() override { return 1; }
    uint32 PLUGIN_API release() override { return 1; }
};

// ---- The hosted instrument ----
VST3::Hosting::Module::Ptr module;
IPtr<PlugProvider> provider;
IPtr<IComponent> component;
IPtr<IAudioProcessor> processor;
IPtr<IEditController> controller;
IPtr<IMidiMapping> midiMapping;
ComponentHandler handler;
PlugFrame frame;
HostApplication hostApp;
std::string pluginName;
std::mutex processLock; // state access and processing don't overlap

bool IsInstrument(const VST3::Hosting::ClassInfo& c)
{
    return c.category() == kVstAudioEffectClass && c.subCategoriesString().find("Instrument") != std::string::npos;
}

int Scan(const std::string& path)
{
    std::string error;
    auto m = VST3::Hosting::Module::create(path, error);
    if (!m) { Reply("ERROR\t" + Clean(error)); return 2; }
    for (auto& c : m->getFactory().classInfos())
        if (IsInstrument(c))
            Reply("CLASS\t" + c.ID().toString() + "\t" + Clean(c.name()) + "\t" + Clean(c.vendor()) + "\t" + Clean(c.subCategoriesString()));
    return 0;
}

bool offline = false; // --offline: a render host for Generate (no deadline; plug-ins may load samples synchronously)

bool Load(const std::string& path, const std::string& uid, std::string& error)
{
    module = VST3::Hosting::Module::create(path, error);
    if (!module) return false;
    auto factory = module->getFactory();
    for (auto& c : factory.classInfos())
    {
        if (c.category() != kVstAudioEffectClass) continue;
        if (uid.empty() ? !IsInstrument(c) : c.ID().toString() != uid) continue;
        provider = owned(new PlugProvider(factory, c, true));
        if (!provider->initialize()) { provider = nullptr; continue; }
        pluginName = c.name();
        break;
    }
    if (!provider) { error = "No VST3 instrument class was found in this plug-in."; return false; }

    component = provider->getComponentPtr();
    controller = provider->getControllerPtr();
    processor = FUnknownPtr<IAudioProcessor>(component);
    if (!component || !processor) { error = "The plug-in has no audio processor."; return false; }
    if (controller)
    {
        controller->setComponentHandler(&handler);
        midiMapping = FUnknownPtr<IMidiMapping>(controller);
    }

    // Stereo main output, the first event input; everything else off.
    int32 outputs = component->getBusCount(kAudio, kOutput), inputs = component->getBusCount(kAudio, kInput);
    if (outputs < 1) { error = "The plug-in has no audio output."; return false; }
    std::vector<SpeakerArrangement> outArr(outputs, SpeakerArr::kStereo), inArr(inputs, SpeakerArr::kStereo);
    processor->setBusArrangements(inputs ? inArr.data() : nullptr, inputs, outArr.data(), outputs);
    for (int32 i = 0; i < outputs; i++) component->activateBus(kAudio, kOutput, i, i == 0);
    for (int32 i = 0; i < inputs; i++) component->activateBus(kAudio, kInput, i, false);
    if (component->getBusCount(kEvent, kInput) > 0) component->activateBus(kEvent, kInput, 0, true);

    ProcessSetup setup{offline ? kOffline : kRealtime, kSample32, MaxFrames, 44100.0};
    if (processor->setupProcessing(setup) != kResultOk) { error = "The plug-in refused 44.1 kHz processing."; return false; }
    if (component->setActive(true) != kResultOk) { error = "The plug-in couldn't be activated."; return false; }
    processor->setProcessing(true);
    return true;
}

// ---- State: component state, then controller state, each with a 4-byte length ----
bool ReadStream(MemoryStream& s, std::vector<uint8_t>& out)
{
    int64 size = 0;
    s.seek(0, IBStream::kIBSeekEnd, &size);
    s.seek(0, IBStream::kIBSeekSet, nullptr);
    out.resize((size_t)size);
    int32 read = 0;
    return size == 0 || s.read(out.data(), (int32)size, &read) == kResultOk;
}

std::string GetState()
{
    std::vector<uint8_t> a, b, all;
    {
        // Only reading the plug-in holds up its audio; encoding happens after.
        std::lock_guard<std::mutex> g(processLock);
        MemoryStream comp, ctrl;
        if (component->getState(&comp) != kResultOk) return "ERROR\tThe plug-in didn't save its state.";
        if (controller) controller->getState(&ctrl);
        ReadStream(comp, a);
        ReadStream(ctrl, b);
    }
    if (a.size() + b.size() > MaxStateBytes) return "ERROR\tThe plug-in's state is larger than 64 MB.";
    all.reserve(1 + 8 + a.size() + b.size());
    all.push_back(1); // format
    for (auto* part : {&a, &b})
    {
        uint32_t n = (uint32_t)part->size();
        for (int i = 0; i < 4; i++) all.push_back((uint8_t)(n >> (8 * i)));
        all.insert(all.end(), part->begin(), part->end());
    }
    return "STATE\t" + ToBase64(all);
}

std::string SetState(const std::string& line, size_t from)
{
    std::vector<uint8_t> all;
    if (!FromBase64(line, from, all) || all.empty() || all[0] != 1) return "ERROR\tThe saved state is damaged or from a newer dawsu!.";
    std::vector<uint8_t> parts[2];
    size_t pos = 1;
    for (auto& part : parts)
    {
        if (pos + 4 > all.size()) return "ERROR\tThe saved state is damaged.";
        uint32_t n = all[pos] | all[pos + 1] << 8 | all[pos + 2] << 16 | (uint32_t)all[pos + 3] << 24;
        pos += 4;
        if (n > all.size() - pos) return "ERROR\tThe saved state is damaged.";
        part.assign(all.begin() + pos, all.begin() + pos + n);
        pos += n;
    }

    std::lock_guard<std::mutex> g(processLock);
    MemoryStream comp((void*)parts[0].data(), (TSize)parts[0].size()), ctrl((void*)parts[1].data(), (TSize)parts[1].size());
    if (component->setState(&comp) != kResultOk) return "ERROR\tThe plug-in rejected its saved state.";
    if (controller)
    {
        comp.seek(0, IBStream::kIBSeekSet, nullptr);
        controller->setComponentState(&comp);
        if (!parts[1].empty()) controller->setState(&ctrl);
    }
    return "OK";
}

// ---- Editor window (on the main thread) ----
LRESULT CALLBACK EditorProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_CLOSE)
    {
        ShowWindow(hwnd, SW_HIDE);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

std::string ShowEditor()
{
    if (!controller) return "ERROR\tThe plug-in has no editor.";
    if (!editorWindow)
    {
        view = owned(controller->createView(ViewType::kEditor));
        if (!view || view->isPlatformTypeSupported(kPlatformTypeHWND) != kResultTrue) { view = nullptr; return "ERROR\tThe plug-in has no editor window."; }
        ViewRect r{0, 0, 800, 600};
        view->getSize(&r);
        RECT rect{0, 0, r.getWidth(), r.getHeight()};
        DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
        AdjustWindowRect(&rect, style, FALSE);
        // The plug-in's name is UTF-8.
        std::wstring title(MultiByteToWideChar(CP_UTF8, 0, pluginName.data(), (int)pluginName.size(), nullptr, 0), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, pluginName.data(), (int)pluginName.size(), title.data(), (int)title.size());
        title += L" — dawsu!";
        editorWindow = CreateWindowW(L"DawsuVst3Editor", title.c_str(), style, CW_USEDEFAULT, CW_USEDEFAULT,
            rect.right - rect.left, rect.bottom - rect.top, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        view->setFrame(&frame);
        if (view->attached(editorWindow, kPlatformTypeHWND) != kResultTrue)
        {
            DestroyWindow(editorWindow);
            editorWindow = nullptr;
            view = nullptr;
            return "ERROR\tThe plug-in's editor didn't open.";
        }
    }
    ShowWindow(editorWindow, SW_SHOW);
    SetForegroundWindow(editorWindow);
    return "OK";
}

// Commands that touch the plug-in's UI run on the main thread: the reader thread hands them over and waits.
constexpr UINT WM_COMMAND_LINE = WM_APP + 1;
struct Pending { std::string id, line, reply; };

std::string Render(const std::string& line);

std::string Run(const std::string& line)
{
    if (line == "SHOW") return ShowEditor();
    if (line == "HIDE") { if (editorWindow) ShowWindow(editorWindow, SW_HIDE); return "OK"; }
    if (line == "GETSTATE") return GetState();
    if (line.rfind("SETSTATE\t", 0) == 0) return SetState(line, 9);
    if (line.rfind("RENDER\t", 0) == 0) return offline ? Render(line) : "ERROR\tThis host plays live; start one with --offline to render.";
    return "ERROR\tUnknown command.";
}

// ---- Processing one block: MIDI-style events in, stereo out (live playback and offline rendering) ----
struct Block
{
    HostProcessData data;
    EventList events{MaxEvents};
    ParameterChanges params{64};
    ProcessContext context{};
    std::map<int, ParamID> mapped; // (channel << 8 | controller) -> parameter, or kNoParamId

    Block()
    {
        data.prepare(*component, MaxFrames, kSample32);
        context.sampleRate = 44100;
        context.tempo = 120;
        context.state = ProcessContext::kPlaying | ProcessContext::kTempoValid;
    }

    ParamID Map(int channel, int cc)
    {
        int key = channel << 8 | cc;
        auto it = mapped.find(key);
        if (it != mapped.end()) return it->second;
        ParamID id = kNoParamId;
        if (midiMapping) midiMapping->getMidiControllerAssignment(0, (int16)channel, (CtrlNumber)cc, id);
        return mapped[key] = id;
    }

    void Change(ParamID id, int offset, ParamValue value)
    {
        if (id == kNoParamId) return;
        int32 index = 0;
        if (auto* queue = params.addParameterData(id, index)) queue->addPoint(offset, value, index);
    }

    // Starts a block: no events yet, plus the edits made in the plug-in's window.
    void Begin()
    {
        events.clear();
        params.clearQueue();
        std::lock_guard<std::mutex> g(handler.lock);
        for (auto& [id, value] : handler.pending) Change(id, 0, value);
        handler.pending.clear();
    }

    void Add(int offset, uint8_t status, uint8_t data1, uint8_t data2)
    {
        int kind = status & 0xF0, channel = status & 0x0F;
        Event ev{};
        ev.busIndex = 0;
        ev.sampleOffset = offset;
        if (kind == 0x90 && data2 > 0)
        {
            ev.type = Event::kNoteOnEvent;
            ev.noteOn.channel = (int16)channel;
            ev.noteOn.pitch = (int16)data1;
            ev.noteOn.velocity = data2 / 127.f;
            ev.noteOn.noteId = -1;
            events.addEvent(ev);
        }
        else if (kind == 0x80 || kind == 0x90)
        {
            ev.type = Event::kNoteOffEvent;
            ev.noteOff.channel = (int16)channel;
            ev.noteOff.pitch = (int16)data1;
            ev.noteOff.noteId = -1;
            events.addEvent(ev);
        }
        else if (kind == 0xB0) Change(Map(channel, data1), offset, data2 / 127.0);
        else if (kind == 0xE0) Change(Map(channel, kPitchBend), offset, (data2 << 7 | data1) / 16383.0);
    }

    // Renders the block into left/right. False when the plug-in failed (the output is then silence).
    bool Process(int frames, float* left, float* right)
    {
        std::lock_guard<std::mutex> g(processLock);
        data.numSamples = frames;
        data.inputEvents = &events;
        data.inputParameterChanges = &params;
        data.processContext = &context;
        bool ok = processor->process(data) == kResultOk && data.numOutputs > 0 && data.outputs[0].numChannels > 0;
        if (ok)
        {
            float* l = data.outputs[0].channelBuffers32[0];
            float* r = data.outputs[0].numChannels > 1 ? data.outputs[0].channelBuffers32[1] : l;
            memcpy(left, l, frames * sizeof(float));
            memcpy(right, r, frames * sizeof(float));
        }
        else
        {
            memset(left, 0, frames * sizeof(float));
            memset(right, 0, frames * sizeof(float));
        }
        context.projectTimeSamples += frames;
        return ok;
    }
};

// ---- Offline rendering of one note (Generate) ----
// RENDER \t <out file> \t <max frames> \t <quiet frames> \t <threshold> \t <frame>:<status>:<d1>:<d2>;...
// Silences the plug-in first (note-offs on every key, then render until quiet), then renders the events from frame 0
// until the output stays below the threshold for <quiet frames> after the last event, or <max frames>. Writes the
// stereo float32 frames (interleaved, little-endian) to the file, cut where the sound fell quiet.
// Reply: RENDERED \t <frames> \t <1 when it hit max frames> \t <peak>.
std::string Render(const std::string& line)
{
    std::vector<std::string> f;
    for (size_t at = 0;;)
    {
        size_t tab = line.find('\t', at);
        f.push_back(line.substr(at, tab == std::string::npos ? std::string::npos : tab - at));
        if (tab == std::string::npos) break;
        at = tab + 1;
    }
    if (f.size() != 6 || f[0] != "RENDER") return "ERROR\tusage: RENDER <file> <max> <quiet> <threshold> <events>";
    long long maxFrames = _atoi64(f[2].c_str()), quietFrames = _atoi64(f[3].c_str());
    double threshold = atof(f[4].c_str());
    if (maxFrames < 1 || maxFrames > 44100LL * 600 || quietFrames < 1 || !(threshold > 0)) return "ERROR\tBad render limits.";

    struct Ev { long long frame; uint8_t s, d1, d2; };
    std::vector<Ev> evs;
    for (size_t at = 0; at < f[5].size();)
    {
        size_t end = f[5].find(';', at);
        if (end == std::string::npos) end = f[5].size();
        long long frame; unsigned st, d1, d2;
        if (sscanf_s(f[5].substr(at, end - at).c_str(), "%lld:%u:%u:%u", &frame, &st, &d1, &d2) == 4 && frame >= 0 && st < 256 && d1 < 128 && d2 < 128)
            evs.push_back({frame, (uint8_t)st, (uint8_t)d1, (uint8_t)d2});
        at = end + 1;
    }
    std::stable_sort(evs.begin(), evs.end(), [](const Ev& a, const Ev& b) { return a.frame < b.frame; });
    long long lastEvent = evs.empty() ? 0 : evs.back().frame;

    constexpr int Chunk = 512;
    static Block block; // one per host: the render host renders nothing else
    std::vector<float> l(Chunk), r(Chunk);
    auto Peak = [&](int n) { float p = 0; for (int i = 0; i < n; i++) p = std::max(p, std::max(std::fabs(l[i]), std::fabs(r[i]))); return p; };

    // Silence what the previous note left.
    block.Begin();
    for (int pitch = 0; pitch < 128; pitch++) block.Add(0, 0x80, (uint8_t)pitch, 0);
    for (long long done = 0, quiet = 0; done < 44100LL * 5 && quiet < quietFrames; done += Chunk)
    {
        block.Process(Chunk, l.data(), r.data());
        quiet = Peak(Chunk) < threshold ? quiet + Chunk : 0;
        block.Begin();
    }

    std::vector<float> out;
    size_t next = 0;
    long long lastLoud = -1, quiet = 0, frame = 0;
    float peak = 0;
    bool truncated = true;
    while (frame < maxFrames)
    {
        int n = (int)std::min<long long>(Chunk, maxFrames - frame);
        block.Begin();
        while (next < evs.size() && evs[next].frame < frame + n)
        {
            block.Add((int)(evs[next].frame - frame), evs[next].s, evs[next].d1, evs[next].d2);
            next++;
        }
        if (!block.Process(n, l.data(), r.data())) return "ERROR\tThe plug-in failed while rendering.";
        for (int i = 0; i < n; i++)
        {
            out.push_back(l[i]);
            out.push_back(r[i]);
            float a = std::max(std::fabs(l[i]), std::fabs(r[i]));
            peak = std::max(peak, a);
            if (a >= threshold) lastLoud = frame + i;
        }
        frame += n;
        quiet = frame - 1 - std::max(lastLoud, lastEvent);
        if (frame > lastEvent && quiet >= quietFrames) { truncated = false; break; }
    }

    long long keep = truncated ? frame : std::max(lastLoud, lastEvent) + 1;
    FILE* file = nullptr;
    std::wstring path(MultiByteToWideChar(CP_UTF8, 0, f[1].data(), (int)f[1].size(), nullptr, 0), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, f[1].data(), (int)f[1].size(), path.data(), (int)path.size());
    if (_wfopen_s(&file, path.c_str(), L"wb") != 0 || !file) return "ERROR\tThe render file couldn't be written.";
    size_t wrote = fwrite(out.data(), sizeof(float), (size_t)keep * 2, file);
    fclose(file);
    if (wrote != (size_t)keep * 2) return "ERROR\tThe render file couldn't be written.";
    char reply[96];
    snprintf(reply, sizeof reply, "RENDERED\t%lld\t%d\t%.6f", keep, truncated ? 1 : 0, peak);
    return reply;
}

// ---- Audio: one block per request ----
std::atomic<bool> quitting{false};

void AudioThread(Shm* shm, HANDLE req, HANDLE done, HANDLE parent)
{
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    Block block;
    HANDLE waits[2] = {req, parent};
    while (!quitting)
    {
        DWORD w = WaitForMultipleObjects(2, waits, FALSE, 500);
        if (w == WAIT_OBJECT_0 + 1) ExitProcess(0); // dawsu! is gone
        if (w != WAIT_OBJECT_0) continue;

        int frames = shm->frames < 1 ? 0 : shm->frames > MaxFrames ? MaxFrames : shm->frames;
        int count = shm->eventCount < 0 ? 0 : shm->eventCount > MaxEvents ? MaxEvents : shm->eventCount;
        block.Begin();
        for (int i = 0; i < count; i++)
        {
            auto& e = shm->events[i];
            int offset = e.offset < 0 ? 0 : e.offset >= frames ? (frames > 0 ? frames - 1 : 0) : e.offset;
            block.Add(offset, e.status, e.data1, e.data2);
        }

        int status = 1;
        if (frames > 0 && block.Process(frames, shm->left, shm->right)) status = 0;
        shm->status = status;
        MemoryBarrier();
        shm->doneSeq = shm->requestSeq;
        SetEvent(done);
    }
}

} // namespace

int wmain(int argc, wchar_t** argv)
{
    auto Arg = [&](int i) {
        int n = WideCharToMultiByte(CP_UTF8, 0, argv[i], -1, nullptr, 0, nullptr, nullptr);
        std::string s(n > 0 ? n - 1 : 0, '\0');
        WideCharToMultiByte(CP_UTF8, 0, argv[i], -1, s.data(), n, nullptr, nullptr);
        return s;
    };
    IsolateStdout();
    if (argc == 3 && Arg(1) == "--scan") return Scan(Arg(2));
    if (argc < 5 || Arg(1) != "--host") { Reply("ERROR\tusage: --scan <plugin.vst3> | --host <name> <parentPid> <plugin.vst3> [<uid>] [--offline]"); return 1; }
    std::string uid;
    for (int i = 5; i < argc; i++)
        if (Arg(i) == "--offline") offline = true;
        else uid = Arg(i);

    std::wstring name = argv[2];
    DWORD parentPid = (DWORD)_wtoi(argv[3]);
    HANDLE parent = OpenProcess(SYNCHRONIZE, FALSE, parentPid);
    if (!parent) { Reply("ERROR\tdawsu! is not running."); return 1; }

    HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, (L"Local\\" + name).c_str());
    HANDLE req = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, FALSE, (L"Local\\" + name + L"-req").c_str());
    HANDLE done = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, FALSE, (L"Local\\" + name + L"-done").c_str());
    auto* shm = mapping ? (Shm*)MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Shm)) : nullptr;
    if (!shm || !req || !done || shm->magic != Magic) { Reply("ERROR\tThe shared audio buffer is missing."); return 1; }
    if (shm->version != Protocol) { Reply("ERROR\tdawsu_vst3_host.exe doesn't match this version of dawsu!; reinstall dawsu!."); return 1; }

    OleInitialize(nullptr); // some plug-ins need COM/OLE on their UI thread
    PluginContextFactory::instance().setPluginContext(&hostApp);
    WNDCLASSW wc{};
    wc.lpfnWndProc = EditorProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"DawsuVst3Editor";
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    RegisterClassW(&wc);

    std::string error;
    if (!Load(Arg(4), uid, error)) { Reply("ERROR\t" + Clean(error)); return 2; }
    Reply("READY\t" + Clean(pluginName) + "\t" + std::to_string(Protocol));

    std::thread audio(AudioThread, shm, req, done, parent);
    DWORD mainThread = GetCurrentThreadId();
    std::thread reader([mainThread] {
        std::string line;
        while (std::getline(std::cin, line))
        {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            size_t tab = line.find('\t');
            if (tab == std::string::npos) continue; // every command is "<id> \t <command>"
            Pending p{line.substr(0, tab), line.substr(tab + 1), ""};
            if (p.line == "QUIT") break;
            HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            PostThreadMessageW(mainThread, WM_COMMAND_LINE, (WPARAM)&p, (LPARAM)ready);
            WaitForSingleObject(ready, INFINITE);
            CloseHandle(ready);
            Reply(p.id + "\t" + p.reply);
        }
        PostThreadMessageW(mainThread, WM_QUIT, 0, 0);
    });

    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0)
    {
        if (msg.hwnd == nullptr && msg.message == WM_COMMAND_LINE)
        {
            auto* p = (Pending*)msg.wParam;
            p->reply = Run(p->line);
            SetEvent((HANDLE)msg.lParam);
            continue;
        }
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    quitting = true;
    audio.join();
    reader.detach();
    if (view) { view->removed(); view = nullptr; }
    if (processor) processor->setProcessing(false);
    if (component) component->setActive(false);
    controller = nullptr;
    processor = nullptr;
    component = nullptr;
    provider = nullptr;
    module = nullptr;
    PluginContextFactory::instance().setPluginContext(nullptr);
    return 0;
}
