using System.Text;
using System.Text.Json;
using NAudio.Wave;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App.Tests;

/// <summary>SoundFont (.sf2) instruments: Keysound tracks and the Hitsound editor's drum kit (issue #3).</summary>
public sealed class SoundFontTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"dawsu-test-{Guid.NewGuid():N}.sf2");

    public SoundFontTests() => File.WriteAllBytes(path, TinySoundFont());

    public void Dispose() => File.Delete(path);

    [Fact]
    public void Presets_are_listed_by_bank_with_the_kit_marked()
    {
        var presets = SoundFonts.Presets(SoundFonts.Load(path));
        Assert.Equal(["000:000 Sine", "128:000 Kit"], presets.Select(p => p.Label));
        Assert.True(presets[1].IsDrumKit);
        Assert.Same(SoundFonts.Load(path), SoundFonts.Load(path)); // read once, shared
    }

    [Theory]
    [InlineData(@"\\server\share\kit.sf2")]
    [InlineData("//server/share/kit.sf2")]
    [InlineData(@"kit.sf2")]
    [InlineData(@"C:\Fonts\kit.exe")]
    public void A_project_cant_point_a_soundfont_at_a_network_share_or_another_file_type(string path)
    {
        // A project from elsewhere must not make dawsu! reach a share (Windows would send the user's login hash).
        Assert.False(SoundFonts.IsLoadablePath(path));
        Assert.ThrowsAny<IOException>(() => SoundFonts.Load(path));
    }

    [Fact]
    public void A_local_soundfont_path_is_loadable() => Assert.True(SoundFonts.IsLoadablePath(path));

    [Fact]
    public void A_file_that_isnt_a_soundfont_is_reported_not_thrown_as_anything_else()
    {
        string bad = Path.GetTempFileName();
        try
        {
            File.Move(bad, bad += ".sf2");
            File.WriteAllBytes(bad, Encoding.ASCII.GetBytes("not a soundfont at all"));
            Assert.ThrowsAny<IOException>(() => SoundFonts.Load(bad));
            Assert.Throws<FileNotFoundException>(() => SoundFonts.Load(bad[..^4] + "-missing.sf2"));
        }
        finally { File.Delete(bad); }
    }

    [Fact]
    public async Task Generate_renders_a_note_with_its_release_and_the_same_each_time()
    {
        var job = new KeyRenderJob(Guid.NewGuid(), Guid.NewGuid(), 69, 100, 200, 200, []);
        using var renderer = new SoundFontNoteRenderer(SoundFonts.Load(path), 0, 0, effects: false);
        var first = await renderer.RenderAsync(job, new RenderLimits());
        var second = await renderer.RenderAsync(job, new RenderLimits());
        Assert.False(first.Truncated);
        Assert.True(first.Frames >= PlaybackMixer.Rate / 5); // at least the held 200 ms
        Assert.True(first.Stereo.Max(Math.Abs) > 0.01f);
        Assert.Equal(first.Stereo, second.Stereo); // a fresh synthesizer per note: no tail carries over
    }

    [Fact]
    public void A_track_part_plays_its_scheduled_notes_and_lets_go_on_release()
    {
        var part = new SoundFontPart(new SoundFontSynth(SoundFonts.Load(path), 0, 0, effects: false));
        part.SetEvents([new TimedMidi(0, 0x90, 69, 100), new TimedMidi(5000, 0x80, 69, 0)]);
        double msPerFrame = 1000.0 / PlaybackMixer.Rate;
        var block = new float[1024 * 2];
        part.Render(block, 1024, 0, msPerFrame, playing: true);
        Assert.True(block.Max(Math.Abs) > 0.01f);

        part.ReleaseAll(); // stopped: the note is let go and fades out
        float last = 1;
        for (int i = 1; i < 200; i++)
        {
            Array.Clear(block);
            part.Render(block, 1024, i * 1024 * msPerFrame, msPerFrame, playing: false);
            last = block.Max(Math.Abs);
        }
        Assert.True(last < 0.001f);
    }

    [Fact]
    public void A_live_message_plays_at_once()
    {
        var part = new SoundFontPart(new SoundFontSynth(SoundFonts.Load(path), 0, 0, effects: true));
        part.Live(0x90, 60, 110);
        var block = new float[512 * 2];
        part.Render(block, 512, 0, 1000.0 / PlaybackMixer.Rate, playing: false);
        Assert.True(block.Max(Math.Abs) > 0.01f);
    }

    [Fact]
    public async Task The_drum_kit_renders_a_one_shot_wav()
    {
        var kit = new SoundFontDrumKit(new SoundFontSynth(SoundFonts.Load(path), 128, 0, effects: false));
        string output = Path.GetTempFileName();
        try
        {
            await DrumVstRenderer.Render(kit, new DrumRenderKey(36, 100), output, CancellationToken.None);
            using var wave = new WaveFileReader(output);
            var samples = new float[(int)(wave.SampleCount * 2)];
            int read = wave.ToSampleProvider().Read(samples);
            Assert.True(samples.Take(read).Max(Math.Abs) > 0.01f);
        }
        finally { File.Delete(output); }
    }

    [Fact]
    public void A_retrigger_keeps_its_note_off_before_its_note_on()
    {
        var kit = new SoundFontDrumKit(new SoundFontSynth(SoundFonts.Load(path), 128, 0, effects: false));
        var buffer = new float[512 * 2];
        kit.Process(buffer, 512, [new(0, 0x99, 36, 100)], 1);
        // Same offset, off then on: the note must sound again afterwards, not be cut.
        kit.Process(buffer, 512, [new(10, 0x89, 36, 0), new(10, 0x99, 36, 100)], 2);
        Assert.True(buffer.Skip(200 * 2).Max(Math.Abs) > 0.01f);
    }

    [Fact]
    public void A_soundfont_track_is_saved_with_its_preset_and_effects()
    {
        var instrument = KeyInstrument.SoundFontPreset(path, 128, 0, "Kit", effects: false);
        var copy = JsonSerializer.Deserialize<KeyInstrument>(JsonSerializer.Serialize(instrument))!;
        Assert.True(copy.IsSoundFont && copy.IsMidiInstrument);
        Assert.Equal((128, 0, false, "Kit"), (copy.Bank, copy.Program, copy.Effects, copy.Name));
        Assert.True(copy.SamePlugin(instrument));
        Assert.False(copy.SamePlugin(instrument with { Effects = true })); // toggling effects makes a new synthesizer
        Assert.Contains("Kit", instrument.DisplayName);
    }

    /// <summary>
    /// A valid, tiny SoundFont: one looped 441 Hz sine (100 samples per cycle at 44.1 kHz), one instrument over the
    /// whole keyboard, and two presets on it, a melodic one (bank 0) and a percussion kit (bank 128).
    /// </summary>
    internal static byte[] TinySoundFont()
    {
        const int frames = 4400;
        var smpl = new MemoryStream();
        var w = new BinaryWriter(smpl);
        for (int i = 0; i < frames; i++) w.Write((short)(Math.Sin(2 * Math.PI * i / 100) * 16000));
        for (int i = 0; i < 46; i++) w.Write((short)0); // the zero samples the format asks for after each sample

        static byte[] Name(string s) { var b = new byte[20]; Encoding.ASCII.GetBytes(s).CopyTo(b, 0); return b; }
        static byte[] Records(Action<BinaryWriter> write) { var m = new MemoryStream(); write(new BinaryWriter(m)); return m.ToArray(); }

        var phdr = Records(b =>
        {
            foreach (var (name, program, bank, bag) in new[] { ("Sine", 0, 0, 0), ("Kit", 0, 128, 1), ("EOP", 0, 0, 2) })
            {
                b.Write(Name(name)); b.Write((ushort)program); b.Write((ushort)bank); b.Write((ushort)bag);
                b.Write(0u); b.Write(0u); b.Write(0u);
            }
        });
        var pbag = Records(b => { for (int i = 0; i < 3; i++) { b.Write((ushort)i); b.Write((ushort)0); } });
        var pmod = new byte[10];
        var pgen = Records(b => { b.Write((ushort)41); b.Write((ushort)0); b.Write((ushort)41); b.Write((ushort)0); b.Write(0u); });
        var inst = Records(b => { b.Write(Name("SineInst")); b.Write((ushort)0); b.Write(Name("EOI")); b.Write((ushort)1); });
        var ibag = Records(b => { b.Write((ushort)0); b.Write((ushort)0); b.Write((ushort)3); b.Write((ushort)0); });
        var imod = new byte[10];
        var igen = Records(b =>
        {
            b.Write((ushort)43); b.Write((byte)0); b.Write((byte)127); // keyRange 0-127
            b.Write((ushort)54); b.Write((ushort)1);                    // sampleModes: loop
            b.Write((ushort)53); b.Write((ushort)0);                    // sampleID 0
            b.Write(0u);
        });
        var shdr = Records(b =>
        {
            b.Write(Name("Sine")); b.Write(0u); b.Write((uint)frames); b.Write(0u); b.Write((uint)frames);
            b.Write(44100u); b.Write((byte)69); b.Write((sbyte)0); b.Write((ushort)0); b.Write((ushort)1);
            b.Write(new byte[46]);
        });

        static byte[] Chunk(string id, byte[] data)
        {
            var m = new MemoryStream(); var b = new BinaryWriter(m);
            b.Write(Encoding.ASCII.GetBytes(id)); b.Write(data.Length); b.Write(data);
            if (data.Length % 2 == 1) b.Write((byte)0);
            return m.ToArray();
        }
        static byte[] List(string type, params byte[][] chunks) => Chunk("LIST", [.. Encoding.ASCII.GetBytes(type), .. chunks.SelectMany(c => c)]);

        var info = List("INFO", Chunk("ifil", [2, 0, 1, 0]), Chunk("isng", Encoding.ASCII.GetBytes("EMU8000\0")), Chunk("INAM", Encoding.ASCII.GetBytes("Test\0\0")));
        var sdta = List("sdta", Chunk("smpl", smpl.ToArray()));
        var pdta = List("pdta", Chunk("phdr", phdr), Chunk("pbag", pbag), Chunk("pmod", pmod), Chunk("pgen", pgen),
            Chunk("inst", inst), Chunk("ibag", ibag), Chunk("imod", imod), Chunk("igen", igen), Chunk("shdr", shdr));
        return Chunk("RIFF", [.. Encoding.ASCII.GetBytes("sfbk"), .. info, .. sdta, .. pdta]);
    }
}
