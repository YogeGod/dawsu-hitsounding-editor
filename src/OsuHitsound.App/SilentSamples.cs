using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// Tells Copy hitsounds which mapset samples play nothing (for "Never create silent hitnormals"): a file that decodes
/// to near-zero samples everywhere, osu!'s empty blank.wav included. A file that is missing or can't be decoded counts
/// as audible, so the option never hides a sound it isn't sure about.
/// </summary>
internal static class SilentSamples
{
    // About -80 dBFS: below anything audible in a hitsound.
    private const float Threshold = 1e-4f;

    /// <summary>A cached check over these folders, tried in order (the source's, then the targets' mapset).</summary>
    public static Func<Lane, bool> Check(params MapFolder[] folders)
    {
        var known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        return lane =>
        {
            if (known.TryGetValue(lane.Name, out bool silent)) return silent;
            silent = false;
            foreach (var folder in folders)
                if (folder.Find(lane.Name) is { } file)
                {
                    silent = IsSilent(folder.ReadBytes(file));
                    break;
                }

            return known[lane.Name] = silent;
        };
    }

    public static bool IsSilent(byte[] file)
    {
        // Decoding needs BASS; the "no sound" device is enough (already initialised by the audio engine is fine).
        if (!ManagedBass.Bass.Init(0) && ManagedBass.Bass.LastError != ManagedBass.Errors.Already) return false;
        return SampleMixer.Decode(file) is { } samples && samples.All(s => Math.Abs(s) < Threshold);
    }
}
