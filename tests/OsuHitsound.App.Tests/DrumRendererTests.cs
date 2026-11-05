using NAudio.Wave;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

namespace OsuHitsound.App.Tests;
public class DrumRendererTests
{
    [Fact]
    public async Task One_shot_keeps_leading_silence_sends_noteoff_and_preserves_tail()
    {
        string output = Path.GetTempFileName(); int sinceNote = -1; var events = new List<(int Frame, MidiEvent Event)>();
        try
        {
            void Process(float[] buffer, int frames, MidiEvent[] midi)
            {
                Array.Clear(buffer);
                if (midi.Any(e => e.Status == 0x99)) sinceNote = 0;
                foreach (var e in midi) if (sinceNote >= 0) events.Add((sinceNote + e.Offset, e));
                if (sinceNote >= 0)
                {
                    for (int i = 0; i < frames; i++) if (sinceNote + i is >= 100 and < 44100) buffer[i * 2] = buffer[i * 2 + 1] = 0.25f;
                    sinceNote += frames;
                }
            }
            await DrumVstRenderer.RenderCore(Process, () => {}, new(36, 90), output, CancellationToken.None, false);
            Assert.Equal(4410, Assert.Single(events, p => p.Event.Status == 0x89).Frame);
            using var wave = new WaveFileReader(output); var audio = wave.ToSampleProvider(); var samples = new float[88200];
            Assert.Equal(samples.Length, audio.Read(samples)); Assert.Equal(0, samples[0]); Assert.Equal(0.25f, samples[200]); Assert.True(wave.TotalTime.TotalSeconds >= 2);
        }
        finally { File.Delete(output); }
    }
    [Fact]
    public async Task Cancellation_and_invalid_audio_stop_the_render()
    {
        string output = Path.GetTempFileName(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrumVstRenderer.RenderCore((b,f,m) => Array.Clear(b), () => {}, new(36,90), output, cancelled.Token, false));
            bool started = false;
            await Assert.ThrowsAsync<IOException>(() => DrumVstRenderer.RenderCore((b,f,m) => { Array.Clear(b); started |= m.Any(e => e.Status == 0x99); if (started) b[0] = float.NaN; }, () => {}, new(36,90), output, CancellationToken.None, false));
        }
        finally { File.Delete(output); }
    }
    [Fact]
    public async Task Ringing_tail_fails_instead_of_installing_a_truncated_sound()
    {
        string output = Path.GetTempFileName(); bool started = false;
        try
        {
            var error = await Assert.ThrowsAsync<IOException>(() => DrumVstRenderer.RenderCore((b,f,m) => { started |= m.Any(e => e.Status == 0x99); Array.Fill(b, started ? 0.1f : 0); }, () => {}, new(49,100), output, CancellationToken.None, false));
            Assert.Contains("15-second", error.Message);
        }
        finally { File.Delete(output); }
    }
}
