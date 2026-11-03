using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OsuHitsound.Audio;

/// <summary>The ways the mixed stream can reach the speakers (Preferences → Audio output).</summary>
public enum OutputKind
{
    /// <summary>Windows' normal low-latency path; other apps keep playing. The default.</summary>
    WasapiShared,

    /// <summary>Takes the device for the lowest latency; other apps go silent while the editor plays.</summary>
    WasapiExclusive,

    /// <summary>An ASIO driver (audio interfaces, ASIO4ALL).</summary>
    Asio,

    DirectSound,

    /// <summary>The oldest Windows path (waveOut).</summary>
    Mme,
}

/// <summary>An open output: the player pulling from the mixer, its latency in ms, and how to stop and release it.</summary>
public sealed record OpenOutput(IWavePlayer Player, Func<double> LatencyMs, string Description, Action Close);

/// <summary>Lists devices per output kind and opens one with NAudio. Device ids: "" is the system default.</summary>
public static class AudioOutputs
{
    public static string Name(OutputKind kind) => kind switch
    {
        OutputKind.WasapiShared => "WASAPI (shared)",
        OutputKind.WasapiExclusive => "WASAPI (exclusive)",
        OutputKind.Asio => "ASIO",
        OutputKind.DirectSound => "DirectSound",
        _ => "MME",
    };

    /// <summary>(id, name) for each device, the system default first (ASIO has no default: its drivers only).</summary>
    public static IReadOnlyList<(string Id, string Name)> Devices(OutputKind kind)
    {
        var list = new List<(string, string)>();
        try
        {
            switch (kind)
            {
                case OutputKind.WasapiShared or OutputKind.WasapiExclusive:
                    list.Add(("", "System default"));
                    using (var e = new MMDeviceEnumerator())
                        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                            list.Add((d.ID, d.FriendlyName));
                    break;
                case OutputKind.Asio:
                    list.AddRange(AsioOut.GetDriverNames().Select(n => (n, n)));
                    break;
                case OutputKind.DirectSound:
                    list.Add(("", "System default"));
                    list.AddRange(DirectSoundOut.Devices.Where(d => d.Guid != Guid.Empty).Select(d => (d.Guid.ToString(), d.Description)));
                    break;
                default:
                    list.Add(("", "System default"));
                    for (int i = 0; i < WaveOut.DeviceCount; i++) list.Add((i.ToString(), WaveOut.GetCapabilities(i).ProductName));
                    break;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            // No such device API on this machine: offer what was found (maybe nothing).
        }

        return list;
    }

    /// <summary>Opens and starts an output pulling from the mixer. Throws when it can't (no driver, format refused).</summary>
    public static OpenOutput Open(OutputKind kind, string? device, int bufferMs, PlaybackMixer mixer)
    {
        if (kind == OutputKind.Asio) return OpenAsio(device, mixer);
        bufferMs = Math.Clamp(bufferMs, 10, 500);
        IWavePlayer player;
        Func<double> latency = () => bufferMs;
        switch (kind)
        {
            case OutputKind.WasapiShared or OutputKind.WasapiExclusive:
            {
                using var e = new MMDeviceEnumerator();
                var endpoint = string.IsNullOrEmpty(device) ? e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : e.GetDevice(device);
                var wasapi = Wasapi(endpoint, kind, bufferMs);
                // Windows mixes at the device's rate: resample there ourselves (WDL) when it isn't the mixer's 44.1 kHz.
                ISampleProvider source = wasapi.DeviceMixFormat is { SampleRate: var deviceRate } && deviceRate != PlaybackMixer.Rate
                    ? new WdlResamplingSampleProvider(mixer, deviceRate)
                    : mixer;
                try
                {
                    wasapi.Init(source.ToWaveProvider());
                }
                catch (Exception) when (kind == OutputKind.WasapiExclusive)
                {
                    // Exclusive mode only takes the device's own formats: 16-bit if float was refused.
                    wasapi.Dispose();
                    wasapi = Wasapi(endpoint, kind, bufferMs);
                    wasapi.Init(source.ToWaveProvider16());
                }

                latency = () => wasapi.CurrentLatency.TotalMilliseconds is > 0 and var ms ? ms : wasapi.LatencyMilliseconds;
                player = wasapi;
                break;
            }

            case OutputKind.DirectSound:
            {
                var ds = Guid.TryParse(device, out var guid) ? new DirectSoundOut(guid, bufferMs) : new DirectSoundOut(bufferMs);
                ds.Init(mixer.ToWaveProvider16());
                player = ds;
                break;
            }
            default:
            {
                // waveOut needs a few buffers queued: split the buffer over them (at least 20 ms each, as it stutters below).
                var mme = new WaveOut { NumberOfBuffers = 3, BufferMilliseconds = Math.Max(20, bufferMs / 3), DeviceNumber = int.TryParse(device, out int n) ? n : -1 };
                mme.Init(mixer.ToWaveProvider16());
                latency = () => mme.CurrentLatency.TotalMilliseconds is > 0 and var ms ? ms : mme.BufferMilliseconds * mme.NumberOfBuffers;
                player = mme;
                break;
            }
        }

        player.Play(); // runs from now on: silence while stopped, so previews sound at once
        var name = Name(kind);
        return new OpenOutput(player, latency, string.IsNullOrEmpty(device) ? name : $"{name}: {DeviceName(kind, device)}", () =>
        {
            player.Stop();
            player.Dispose();
        });
    }

    private const int AsioTimeoutMs = 5000;

    /// <summary>
    /// ASIO, on a thread of its own that lives as long as the output (ASIO drivers are COM objects tied to the thread
    /// that made them). Some drivers never answer: after 5 s it gives up, so the editor falls back instead of freezing.
    /// The driver is asked first whether it starts and takes 44.1 kHz, and only then opened by NAudio's AsioOut: an
    /// AsioOut that fails half-way crashes the program when it is cleaned up later.
    /// </summary>
    private static OpenOutput OpenAsio(string? device, PlaybackMixer mixer)
    {
        var names = AsioOut.GetDriverNames();
        if (names.Length == 0) throw new InvalidOperationException("No ASIO driver is installed.");
        var driver = string.IsNullOrEmpty(device) || !names.Contains(device) ? names[0] : device;

        var gate = new object();
        var ready = new ManualResetEventSlim();
        var close = new ManualResetEventSlim();
        OpenOutput? opened = null;
        Exception? failure = null;
        bool abandoned = false;
        var thread = new Thread(() =>
        {
            AsioOut? asio = null;
            try
            {
                Check(driver);
                asio = new AsioOut(driver);
                asio.Init(mixer.ToWaveProvider());
                asio.Play();
                var player = asio;
                lock (gate)
                {
                    if (abandoned) throw new TimeoutException(); // answered too late: nobody wants it now
                    opened = new OpenOutput(player, () => player.PlaybackLatency * 1000.0 / PlaybackMixer.Rate, $"ASIO: {driver}", () =>
                    {
                        close.Set();
                    });
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (opened is null)
            {
                asio?.Dispose();
                return;
            }

            close.Wait(); // keep the driver's thread alive until the output closes
            asio!.Stop();
            asio.Dispose();
        }) { IsBackground = true, Name = "ASIO output" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(AsioTimeoutMs))
        {
            lock (gate) abandoned = true;
            throw new TimeoutException($"the ASIO driver '{driver}' didn't answer in {AsioTimeoutMs / 1000} s");
        }

        return opened ?? throw new InvalidOperationException(failure?.Message ?? "the ASIO driver didn't start", failure);
    }

    // Asks the driver itself whether it starts and plays 44.1 kHz, then lets it go.
    private static void Check(string driverName)
    {
        var driver = NAudio.Wave.Asio.AsioDriver.GetAsioDriverByName(driverName);
        try
        {
            if (!driver.Init(IntPtr.Zero)) throw new InvalidOperationException(driver.GetErrorMessage() is { Length: > 0 } m ? m.Trim() : "the driver didn't start");
            if (!driver.CanSampleRate(PlaybackMixer.Rate)) throw new InvalidOperationException($"the driver doesn't play {PlaybackMixer.Rate} Hz");
        }
        finally
        {
            driver.ReleaseComAsioDriver();
        }
    }

    // Event-driven with "Pro Audio" thread priority; Windows' low-latency mode when the device offers it.
    private static WasapiPlayer Wasapi(MMDevice endpoint, OutputKind kind, int bufferMs)
    {
        var builder = new WasapiPlayerBuilder().WithDevice(endpoint).WithLatency(bufferMs).WithEventSync()
            .WithMmcssThreadPriority("Pro Audio").WithLowLatency(false);
        return (kind == OutputKind.WasapiExclusive ? builder.WithExclusiveMode() : builder.WithSharedMode()).Build();
    }

    private static string DeviceName(OutputKind kind, string device) =>
        Devices(kind).FirstOrDefault(d => d.Id == device).Name ?? device;
}
