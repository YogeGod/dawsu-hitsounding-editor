using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>
/// Renders mixes from one job's <see cref="LaneAudio"/>, decoding each lane once however many mixes use it.
/// One job, one thread at a time, like the lane audio it reads.
/// </summary>
public sealed class LaneMixer(LaneAudio audio)
{
    private readonly Dictionary<Lane, float[]?> decoded = [];

    /// <summary>The lane as interleaved stereo at 44.1 kHz, or null when its audio is missing or can't be decoded.</summary>
    public float[]? Decoded(Lane lane)
    {
        if (decoded.TryGetValue(lane, out var known)) return known;
        return decoded[lane] = audio.TryRead(lane) is { } bytes ? SampleMixer.Decode(bytes) : null;
    }

    /// <summary>The mix as WAV bytes; throws <see cref="IOException"/> when one of its sounds can't be decoded.</summary>
    public byte[] Mix(MixFile mix) => SampleMixer.Mix(mix, Decoded);
}
