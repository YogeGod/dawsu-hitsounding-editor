#include <juce_audio_processors/juce_audio_processors.h>
#include <algorithm>
#include <cstring>
#include <memory>
#include <mutex>
#include <thread>

// Lifecycle, state, editor and offline processing belong to the creating STA.
// The separately configured realtime entry point has one audio consumer. Detach
// from PlaybackMixer before owner state/configuration changes or destruction.
#define API extern "C" __declspec(dllexport)
struct MidiEvent { int offset, status, data1, data2; };
namespace {
struct Runtime {
    juce::ScopedJuceInitialiser_GUI gui;
};
std::weak_ptr<Runtime> runtime;
std::mutex runtimeGate;
std::thread::id runtimeOwner;
struct Host {
    std::thread::id owner = std::this_thread::get_id();
    std::shared_ptr<Runtime> lifetime;
    std::unique_ptr<juce::AudioPluginInstance> plugin;
    std::unique_ptr<juce::DocumentWindow> window;
    juce::AudioBuffer<float> buffer;
    juce::MidiBuffer midi;
    juce::MemoryBlock capturedState;
    int block = 0;
    double rate = 44100;
    bool prepared = false;
    bool realtime = false;
    ~Host() { window.reset(); if (prepared) plugin->releaseResources(); plugin.reset(); }
};
Host* checked(void* p) {
    auto* h = static_cast<Host*>(p);
    return h && h->owner == std::this_thread::get_id() ? h : nullptr;
}
void errorText(char* dst, int capacity, const juce::String& text) {
    if (dst && capacity > 0) text.copyToUTF8(dst, static_cast<size_t>(capacity));
}
bool prepare(Host& h) {
    if (h.prepared) return true;
    auto layout = h.plugin->getBusesLayout();
    for (auto& input : layout.inputBuses) input = juce::AudioChannelSet::disabled();
    for (auto& output : layout.outputBuses) output = juce::AudioChannelSet::disabled();
    if (layout.outputBuses.isEmpty()) return false;
    layout.outputBuses.getReference(0) = juce::AudioChannelSet::stereo();
    if (!h.plugin->setBusesLayout(layout) || h.plugin->getMainBusNumOutputChannels() != 2) return false;
    h.buffer.setSize(std::max(2, h.plugin->getTotalNumOutputChannels()), h.block);
    h.plugin->setNonRealtime(!h.realtime);
    h.plugin->prepareToPlay(h.rate, h.block);
    h.prepared = true;
    return true;
}
}
// Discovery belongs to the short-lived scanner process, never the render process.
API int dv_scan(const char* path, char* descriptor, int capacity) {
    if (!path || !descriptor || capacity < 2 || capacity > 65536) return -1;
    try {
        juce::ScopedJuceInitialiser_GUI gui;
        juce::VST3PluginFormat format;
        juce::OwnedArray<juce::PluginDescription> types;
        format.findAllTypesForFile(types, juce::String::fromUTF8(path));
        for (const auto* type : types) if (type->isInstrument) {
            const auto xml = type->createXml()->toString();
            if (xml.getNumBytesAsUTF8() >= static_cast<size_t>(capacity)) return -1;
            xml.copyToUTF8(descriptor, static_cast<size_t>(capacity));
            return static_cast<int>(xml.getNumBytesAsUTF8());
        }
    } catch (...) {}
    return -1;
}
API void* dv_open_v2(const char* path, const char* descriptor, int descriptorBytes, double rate, int block, char* error, int capacity) {
    if (!path || !descriptor || descriptorBytes < 1 || descriptorBytes > 65535 || !std::isfinite(rate) || rate < 8000 || rate > 384000 || block < 16 || block > 8192) {
        errorText(error, capacity, "Invalid host configuration"); return nullptr;
    }
    try {
        auto h = std::make_unique<Host>();
        {
            const std::lock_guard<std::mutex> lock(runtimeGate);
            if (runtimeOwner != std::thread::id{} && runtimeOwner != h->owner) {
                errorText(error, capacity, "All VST3 instances must share one owner thread for the process lifetime"); return nullptr;
            }
            runtimeOwner = h->owner;
            h->lifetime = runtime.lock();
            if (!h->lifetime) { h->lifetime = std::make_shared<Runtime>(); runtime = h->lifetime; }
        }
        juce::VST3PluginFormat format;
        juce::PluginDescription instrument;
        const auto xml = juce::XmlDocument::parse(juce::String::fromUTF8(descriptor, descriptorBytes));
        if (!xml || !instrument.loadFromXml(*xml) || !instrument.isInstrument || instrument.pluginFormatName != "VST3"
            || juce::File(instrument.fileOrIdentifier) != juce::File(juce::String::fromUTF8(path))) {
            errorText(error, capacity, "Invalid scanned VST3 instrument description"); return nullptr;
        }
        juce::String reason;
        h->plugin = format.createInstanceFromDescription(instrument, rate, block, reason);
        if (!h->plugin) { errorText(error, capacity, reason); return nullptr; }
        h->block = block;
        h->rate = rate;
        h->midi.ensureSize(65536);
        // Establish processing configuration before presets can start sample loading.
        prepare(*h);
        return h.release();
    } catch (const std::exception& ex) { errorText(error, capacity, ex.what()); }
      catch (...) { errorText(error, capacity, "Plugin threw during load"); }
    return nullptr;
}
API int dv_close(void* ptr) {
    if (auto* h = checked(ptr)) {
        delete h;
        return 1;
    }
    return 0;
}
static int process(Host* h, float* stereo, int frames, const MidiEvent* events, int count) {
    if (!h || !stereo || frames < 1 || frames > h->block || count < 0 || count > 4096 || (count && !events)) return 0;
    try {
        if (!prepare(*h)) return 0;
        h->buffer.clear(); h->midi.clear();
        for (int i = 0; i < count; ++i) {
            const auto& e = events[i];
            if (e.offset < 0 || e.offset >= frames || e.status < 0x80 || e.status >= 0xf0
                || e.data1 < 0 || e.data1 > 127 || e.data2 < 0 || e.data2 > 127) return 0;
            const auto status = static_cast<juce::uint8>(e.status);
            const auto kind = status & 0xf0;
            const juce::uint8 bytes[] { status, static_cast<juce::uint8>(e.data1), static_cast<juce::uint8>(e.data2) };
            h->midi.addEvent(bytes, kind == 0xc0 || kind == 0xd0 ? 2 : 3, e.offset);
        }
        juce::AudioBuffer<float> view(h->buffer.getArrayOfWritePointers(), h->buffer.getNumChannels(), frames);
        h->plugin->processBlock(view, h->midi);
        for (int i = 0; i < frames; ++i) {
            stereo[2*i] = view.getSample(0, i); stereo[2*i+1] = view.getSample(1, i);
            if (!std::isfinite(stereo[2*i]) || !std::isfinite(stereo[2*i+1])) return 0;
        }
        return 1;
    } catch (...) { return 0; }
}
API int dv_process(void* ptr, float* stereo, int frames, const MidiEvent* events, int count) {
    return process(checked(ptr), stereo, frames, events, count);
}
// Configure on the owner thread while detached from the mixer. The caller must
// detach (and wait for any audio callback) before state changes or destruction.
API int dv_realtime(void* ptr) {
    auto* h = checked(ptr);
    if (!h) return 0;
    try {
        if (h->prepared) h->plugin->releaseResources();
        h->prepared = false;
        h->realtime = true;
        return prepare(*h) ? 1 : 0;
    } catch (...) { return 0; }
}
// Single audio consumer; no GUI dispatch, allocation or lazy preparation here.
API int dv_process_realtime(void* ptr, float* stereo, int frames, const MidiEvent* events, int count) {
    auto* h = static_cast<Host*>(ptr);
    if (!h || !h->realtime || !h->prepared) return 0;
    return process(h, stereo, frames, events, count);
}
API int dv_state(void* ptr, void* data, int size, int restore) {
    auto* h = checked(ptr);
    if (!h || size < 0 || size > 64*1024*1024) return -1;
    try {
        if (restore) {
            if (!data || size == 0) return -1;
            if (!prepare(*h)) return -1;
            h->capturedState.reset();
            h->plugin->setStateInformation(data, size); return size;
        }
        if (!data) {
            h->capturedState.reset();
            h->plugin->getStateInformation(h->capturedState);
        }
        const auto required = h->capturedState.getSize();
        if (required == 0 || required > 64*1024*1024) { h->capturedState.reset(); return -1; }
        if (data && static_cast<size_t>(size) >= required) {
            std::memcpy(data, h->capturedState.getData(), required);
            h->capturedState.reset();
        }
        return static_cast<int>(required);
    } catch (...) { return -1; }
}
API int dv_editor(void* ptr, int visible) {
    auto* h = checked(ptr);
    if (!h) return 0;
    try {
        if (!visible) { h->window.reset(); return 1; }
        if (!h->window) {
            auto* editor = h->plugin->createEditorIfNeeded();
            if (!editor) return 0;
            struct Window : juce::DocumentWindow {
                Window(const juce::String& name) : DocumentWindow(name, juce::Colours::black, closeButton) {}
                void closeButtonPressed() override { setVisible(false); }
            };
            h->window = std::make_unique<Window>("dawsu! VST3 - " + h->plugin->getName());
            h->window->setUsingNativeTitleBar(true);
            h->window->setContentOwned(editor, true);
            h->window->centreWithSize(editor->getWidth(), editor->getHeight());
            // Large third-party editors may exceed the display; keep their menu/title reachable.
            h->window->setTopLeftPosition(20, 20);
        }
        h->window->setVisible(true); h->window->toFront(true); return 1;
    } catch (...) { return 0; }
}
API int dv_pump(void* ptr, int milliseconds) {
    if (!checked(ptr) || milliseconds < 1 || milliseconds > 100) return 0;
    juce::MessageManager::getInstance()->runDispatchLoopUntil(milliseconds); return 1;
}
