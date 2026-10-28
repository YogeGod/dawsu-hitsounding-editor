# Native Windows audio output

AudioHost.exe is the editor-owned x64 helper. Requires Windows and .NET Framework 4.8. Build offline with `powershell -File build.ps1`; included DLLs are pinned NAudio 2.2.1 and supporting Microsoft libraries. See dependencies.json and licence files. NAudio: https://github.com/naudio/NAudio/tree/v2.2.1 (MIT, Mark Heath and contributors). Microsoft supporting libraries retain their included MIT notices.

The helper opens playback outputs only. It never records and does not install or replace drivers. ASIO enumerates installed 64-bit drivers and uses their preferred hardware buffer. Stereo output pair is selected with channelOffset. WASAPI offers shared/exclusive modes; DirectSound and MME use 16-bit output converted from the float editor mix. Shared WASAPI may resample to the Windows device format.

The editor mixes its existing graph through an AudioWorklet and transfers interleaved float stereo blocks over local Electron IPC and the helper's stdin pipe. Browser output is silent while native routing is active. This bridge and native prebuffering add latency, so ASIO hardware buffer size alone is not an end-to-end latency measurement. The worklet, pipe and managed driver adapters are not a hard real-time audio engine. Device/IPC errors release native output and recover to browser output. Underrun/overflow counters are available in Audio preferences.

Protocol: input packets contain a one-byte kind (1 JSON command, 2 stereo float PCM), a four-byte little-endian payload length, and payload. Payloads are bounded. JSON commands use request IDs; stdout contains JSON responses and asynchronous audio errors. Commands: devices, configure, stats, controlPanel, close. No arbitrary file or process commands exist. The helper exits and releases output when stdin closes. Only this owned child is terminated if shutdown stalls.

Verification distinguishes device enumeration, successful output initialization, silent PCM consumption, and audible listening. ASIO/exclusive-driver playback must be checked with the user's selected hardware. No driver is proven operational just by appearing in the device list.
