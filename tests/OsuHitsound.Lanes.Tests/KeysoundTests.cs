using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Lanes.Tests;

public class TempoMapTests
{
    [Fact]
    public void Ticks_follow_red_lines_and_accumulate_across_tempo_changes()
    {
        // 120 BPM from 1000 ms, then 60 BPM from 3000 ms (4 beats later).
        var map = new TempoMap([(1000, 500, 4), (3000, 1000, 3)]);

        Assert.Equal(0, map.TickAt(1000));
        Assert.Equal(4 * TempoMap.Ppq, map.TickAt(3000));
        Assert.Equal(5 * TempoMap.Ppq, map.TickAt(4000), 6);
        Assert.Equal(-TempoMap.Ppq, map.TickAt(500), 6); // the first tempo extends backwards
        Assert.Equal(4000, map.TimeAt(5 * TempoMap.Ppq), 6);
        Assert.Equal(1250, map.TimeAt(TempoMap.Ppq / 2), 6);
        Assert.Equal(map.TimeAt(map.TickAt(3456.7)), 3456.7, 6);
    }

    [Fact]
    public void A_tempo_change_keeps_beat_positions_and_moves_times()
    {
        long tick = new TempoMap([(0, 500, 4)]).RoundTick(2000); // beat 4
        Assert.Equal(4000, new TempoMap([(0, 1000, 4)]).TimeAt(tick), 6);
    }

    [Fact]
    public void Invalid_and_duplicate_red_lines_are_ignored_and_the_later_one_wins()
    {
        var map = new TempoMap([(0, -5, 4), (100, 400, 4), (100, 300, 7), (200, double.NaN, 4)]);
        Assert.Single(map.Segments);
        Assert.Equal((100.0, 300.0, 7), (map.Segments[0].Time, map.Segments[0].BeatLength, map.Segments[0].Meter));
        Assert.Equal(500, new TempoMap([]).Segments[0].BeatLength); // no timing: 120 BPM
    }

    [Fact]
    public void Snap_counts_from_each_red_line_and_bar_beat_restarts_at_red_lines()
    {
        var map = new TempoMap([(0, 500, 4), (1100, 600, 3)]);
        Assert.Equal(250, map.Snap(240, 1, 2));
        Assert.Equal(1100, map.Snap(1080, 1, 1)); // never past the next red line
        Assert.Equal(1400, map.Snap(1390, 1, 2)); // grid from the second red line
        Assert.Equal((1, 1), map.BarBeatAt(0));
        Assert.Equal((1, 3), map.BarBeatAt(1000));
        Assert.Equal((2, 3), map.BarBeatAt(2500)); // 1400 ms into the 3/4 section: 2 beats
        Assert.Equal((2, 1), map.BarBeatAt(1100)); // the partial bar before the red line counts as one bar
        Assert.Equal((3, 1), map.BarBeatAt(1100 + 3 * 600));
    }

    [Fact]
    public void Grid_lines_mark_bars_beats_and_subdivisions()
    {
        var lines = new TempoMap([(0, 500, 4)]).GridLines(0, 2000, 2).ToList();
        Assert.Equal(9, lines.Count);
        Assert.Equal((0.0, 0), lines[0]);
        Assert.Equal((250.0, 2), lines[1]);
        Assert.Equal((500.0, 1), lines[2]);
        Assert.Equal((2000.0, 0), lines[8]);

        // Before the first red line its grid extends backwards; a later red line restarts the bar.
        var two = new TempoMap([(1000, 500, 4), (2100, 400, 3)]).GridLines(0, 2500, 1).ToList();
        Assert.Equal([(0.0, 1), (500.0, 1), (1000.0, 0), (1500.0, 1), (2000.0, 1), (2100.0, 0), (2500.0, 1)], two);
    }
}

public class KeysoundSessionTests
{
    private static (KeysoundSession Session, Guid Track) New()
    {
        var track = KeyTrack.Create("Piano");
        return (new KeysoundSession(new KeysoundState([track])), track.Id);
    }

    private static KeyNote Note(long start, int pitch = 60) => new(Guid.NewGuid(), start, 480, pitch, 100);

    [Fact]
    public void Each_command_is_one_undo_step_and_undo_returns_to_saved()
    {
        var (s, track) = New();
        var note = Note(0);
        s.AddNotes(track, [note]);
        Assert.True(s.IsEdited);
        s.UpdateNotes(track, [note with { Pitch = 62, Velocity = 90 }], "Transpose");
        Assert.Equal("Transpose", s.UndoLabel);
        s.Undo();
        Assert.Equal(60, s.State.Track(track)!.Notes[0].Pitch);
        s.Undo();
        Assert.Empty(s.State.Track(track)!.Notes);
        Assert.False(s.IsEdited);
        s.Redo();
        Assert.Single(s.State.Track(track)!.Notes);
    }

    [Fact]
    public void Changing_the_instrument_is_one_step_and_keeps_notes_and_controllers()
    {
        var (s, track) = New();
        s.AddNotes(track, [Note(0)]);
        s.ReplaceTrack(s.State.Track(track)! with { Controllers = [new KeyController(0, ControllerKind.Sustain, 127)] }, "Record");
        var before = s.State.Track(track)!;
        var synth = KeyInstrument.Vst3Plugin(@"C:\VST3\Synth.vst3", "0123456789ABCDEF0123456789ABCDEF", "Synth");
        s.ReplaceInstrument(track, synth, "Change instrument");
        var after = s.State.Track(track)!;
        Assert.Equal(synth, after.Instrument);
        Assert.Equal("Synth", after.Instrument.DisplayName);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.Controllers, after.Controllers);
        Assert.Equal("Change instrument", s.UndoLabel);
        s.ReplaceInstrument(track, synth); // the same instrument: no step
        Assert.Equal("Change instrument", s.UndoLabel);
        s.Undo();
        Assert.Equal(KeyInstrument.Piano, s.State.Track(track)!.Instrument);
    }

    [Fact]
    public void Plug_in_settings_captured_on_save_are_no_undo_step_and_keep_the_session_saved()
    {
        var synth = KeyTrack.Create("Synth") with { Instrument = KeyInstrument.Vst3Plugin(@"C:\VST3\Synth.vst3", null, "Synth") };
        var piano = KeyTrack.Create("Piano");
        var s = new KeysoundSession(new KeysoundState([synth, piano]));
        s.StoreInstrumentStates(new Dictionary<Guid, string> { [synth.Id] = "AQID", [piano.Id] = "ignored" });
        Assert.Equal("AQID", s.State.Track(synth.Id)!.Instrument.State);
        Assert.Null(s.State.Track(piano.Id)!.Instrument.State); // only plug-ins have settings
        Assert.False(s.IsEdited);
        Assert.False(s.CanUndo);

        s.AddNotes(piano.Id, [Note(0)]);
        s.StoreInstrumentStates(new Dictionary<Guid, string> { [synth.Id] = "BAUG" });
        Assert.True(s.IsEdited); // still edited: the notes aren't saved yet
        Assert.Equal("Draw note", s.UndoLabel);
    }

    [Fact]
    public void A_drag_gesture_is_one_step_from_where_it_began()
    {
        var (s, track) = New();
        var note = Note(0);
        s.AddNotes(track, [note]);
        s.BeginGesture("Move notes");
        for (int i = 1; i <= 5; i++)
        {
            s.ResetToGestureStart();
            s.UpdateNotes(track, [note with { Start = i * 100 }], "Move notes");
        }
        s.EndGesture();
        Assert.Equal(500, s.State.Track(track)!.Notes[0].Start);
        s.Undo();
        Assert.Equal(0, s.State.Track(track)!.Notes[0].Start);
        Assert.Equal("Draw note", s.UndoLabel);
    }

    [Fact]
    public void Deleting_a_track_is_undone_with_its_notes_and_settings()
    {
        var (s, track) = New();
        s.AddNotes(track, [Note(0), Note(960, 64)]);
        s.SetIncludeInGenerate(track, false);
        var before = s.State.Track(track);
        s.DeleteTrack(track);
        Assert.Empty(s.State.Tracks);
        s.Undo();
        Assert.Same(before, s.State.Track(track));
    }

    [Fact]
    public void Mute_and_solo_are_playback_only()
    {
        var (s, track) = New();
        var other = s.AddTrack("Strings");
        s.MarkSaved(s.State);
        s.Soloed.Add(other.Id);
        Assert.False(s.IsAudible(track));
        Assert.True(s.IsAudible(other.Id));
        s.Muted.Add(other.Id);
        Assert.False(s.IsAudible(other.Id));
        Assert.False(s.IsEdited);
    }

    [Fact]
    public void No_op_changes_add_no_undo_step()
    {
        var (s, track) = New();
        s.RenameTrack(track, "Piano");
        s.RemoveNotes(track, new HashSet<Guid> { Guid.NewGuid() });
        s.SetIncludeInGenerate(track, true);
        Assert.False(s.CanUndo);
    }
}

public class KeysoundStoreTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("dawsu-keysound-").FullName;
    private string Mapset => Path.Combine(root, "Songs", "1 Artist - Song");

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string MakeMap(string audio = "audio.mp3", string extraEvents = "")
    {
        Directory.CreateDirectory(Path.Combine(Mapset, "sb"));
        var osu = Path.Combine(Mapset, "Artist - Song (Me) [Hard].osu");
        File.WriteAllText(osu, $"""
            osu file format v14

            [General]
            AudioFilename: {audio}

            [Difficulty]
            SliderMultiplier:1.4
            SliderTickRate:1

            [Events]
            0,0,"BG.jpg",0,0
            Video,0,"clip.mp4"
            Sprite,Foreground,Centre,"sb\star.png",320,240
            Animation,Foreground,Centre,"sb/spin.png",320,240,2,100,LoopForever
            Sprite,Foreground,Centre,"..\..\outside.png",320,240
            {extraEvents}

            [TimingPoints]
            1000,500,4,1,0,100,1,0
            3000,-100,4,1,0,100,0,0

            [HitObjects]
            256,192,1000,5,0,0:0:0:0:
            256,192,1500,1,0,0:0:0:0:
            256,192,2000,2,0,L|356:192,1,100,0|0,0:0|0:0,0:0:0:0:
            256,192,4000,12,0,5000,0:0:0:0:
            """);
        foreach (var name in new[] { "audio.mp3", "bg.jpg", "clip.mp4", "sb/star.png", "sb/spin0.png", "sb/spin1.png", "other.wav" })
            File.WriteAllBytes(Path.Combine(Mapset, name), Encoding.UTF8.GetBytes(name));
        File.WriteAllText(Path.Combine(Mapset, "Artist - Song (Me).osb"), "[Events]\nSprite,Background,Centre,\"sb/star.png\",0,0\n");
        File.WriteAllBytes(Path.Combine(root, "Songs", "outside.png"), [1]);
        return osu;
    }

    [Fact]
    public void Create_copies_the_reference_song_and_visuals_but_nothing_else()
    {
        var osu = MakeMap();
        var source = File.ReadAllBytes(osu);
        var warnings = new List<string>();
        var project = KeysoundStore.Create(osu, Path.Combine(root, "Project"), warnings);

        var reference = Path.Combine(root, "Project", "reference");
        var copied = Directory.EnumerateFiles(reference, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(reference, f).Replace('\\', '/')).Order().ToList();
        // Found case-insensitively, copied under the name on disk ("BG.jpg" in the map is bg.jpg); other.wav isn't a visual.
        Assert.Equal(["Artist - Song (Me) [Hard].osu", "Artist - Song (Me).osb", "audio.mp3", "bg.jpg", "clip.mp4", "sb/spin0.png", "sb/spin1.png", "sb/star.png"],
            copied.Order(StringComparer.Ordinal));
        Assert.Contains(warnings, w => w.Contains("outside.png"));
        Assert.Equal(source, File.ReadAllBytes(osu)); // the source map is untouched
        Assert.Equal(Path.Combine(root, "Project", KeysoundStore.ProjectFileName), project);
    }

    [Fact]
    public void A_project_reopens_with_its_tracks_notes_and_playback_toggles()
    {
        var project = KeysoundStore.Create(MakeMap(), Path.Combine(root, "Project"), []);
        var doc = KeysoundStore.Open(project);
        var track = doc.Session.State.Tracks.Single();
        Assert.Equal(KeyInstrument.Piano, track.Instrument);
        Assert.True(track.IncludeInGenerate);

        var note = new KeyNote(Guid.NewGuid(), 960, 240, 64, 87);
        doc.Session.AddNotes(track.Id, [note]);
        var second = doc.Session.AddTrack("Bass");
        doc.Session.SetIncludeInGenerate(second.Id, false);
        doc.Session.Muted.Add(track.Id);
        doc.SelectedTrack = second.Id;
        KeysoundStore.Save(doc);
        Assert.False(doc.Session.IsEdited);

        var again = KeysoundStore.Open(project);
        Assert.Equal(doc.Session.State.Tracks.Select(t => (t.Id, t.Name, t.IncludeInGenerate)), again.Session.State.Tracks.Select(t => (t.Id, t.Name, t.IncludeInGenerate)));
        Assert.Equal(note, again.Session.State.Track(track.Id)!.Notes.Single());
        Assert.Contains(track.Id, again.Session.Muted);
        Assert.Equal(second.Id, again.SelectedTrack);
        Assert.NotNull(again.SongPath);
        Assert.Empty(again.Warnings);
    }

    [Fact]
    public void Open_reads_timing_and_gameplay_objects_from_the_snapshot()
    {
        var doc = KeysoundStore.Open(KeysoundStore.Create(MakeMap(), Path.Combine(root, "Project"), []));
        Assert.Equal(1000, doc.Tempo.Segments.Single().Time);
        Assert.Equal([HitObjectKind.Circle, HitObjectKind.Circle, HitObjectKind.Slider, HitObjectKind.Spinner], doc.Objects.Select(o => o.Kind));
        Assert.Equal([1, 2, 3, 0], doc.Objects.Select(o => o.Combo));
        var slider = doc.Objects[2];
        Assert.Equal([HitPosition.SliderHead, HitPosition.SliderTail], slider.Nodes.Select(n => n.Position));
        Assert.Equal((4000.0, 5000.0), (doc.Objects[3].Start, doc.Objects[3].End));

        // Sync targets: every circle, slider node and spinner end, by time, with its place in [HitObjects].
        Assert.Equal([HitPosition.Circle, HitPosition.Circle, HitPosition.SliderHead, HitPosition.SliderTail, HitPosition.SpinnerEnd],
            doc.Targets.Select(t => t.Position));
        Assert.Equal([(0, 0), (1, 0), (2, 0), (2, 1), (3, 0)], doc.Targets.Select(t => (t.ObjectIndex, t.Node)));
        Assert.Equal(5000, doc.Targets[^1].Time);

        // A synced note keeps its target through Save and Open.
        var track = doc.Session.State.Tracks.Single();
        var note = new KeyNote(Guid.NewGuid(), doc.Tempo.RoundTick(1000), 240, 60, 100) { Target = doc.Targets[0] };
        doc.Session.AddNotes(track.Id, [note]);
        KeysoundStore.Save(doc);
        Assert.Equal(doc.Targets[0], KeysoundStore.Open(doc.ProjectPath).Session.State.Track(track.Id)!.Notes.Single().Target);
    }

    [Fact]
    public void A_missing_song_is_a_warning_and_a_used_folder_is_refused()
    {
        var warnings = new List<string>();
        var osu = MakeMap(audio: "missing.mp3");
        var project = KeysoundStore.Create(osu, Path.Combine(root, "Project"), warnings);
        Assert.Contains(warnings, w => w.Contains("missing.mp3"));
        Assert.Null(KeysoundStore.Open(project).SongPath);
        Assert.Throws<IOException>(() => KeysoundStore.Create(osu, Path.Combine(root, "Project"), []));
        Assert.Throws<IOException>(() => KeysoundStore.Create(osu, Path.Combine(Mapset, "Keys"), []));
        Assert.False(Directory.Exists(Path.Combine(Mapset, "Keys")));
    }

    [Fact]
    public void A_vst3_instrument_reopens_with_its_class_name_and_settings()
    {
        var project = KeysoundStore.Create(MakeMap(), Path.Combine(root, "Project"), []);
        var doc = KeysoundStore.Open(project);
        var id = doc.Session.State.Tracks.Single().Id;
        var synth = KeyInstrument.Vst3Plugin(@"C:\Missing\Synth.vst3", "0123456789ABCDEF0123456789ABCDEF", "Synth") with { State = "AQIDBA==" };
        doc.Session.ReplaceInstrument(id, synth);
        KeysoundStore.Save(doc);

        // A plug-in that isn't installed opens fine: the track keeps it and its settings.
        Assert.Equal(synth, KeysoundStore.Open(project).Session.State.Track(id)!.Instrument);
    }

    [Fact]
    public void Damaged_projects_are_refused()
    {
        var project = KeysoundStore.Create(MakeMap(), Path.Combine(root, "Project"), []);
        var text = File.ReadAllText(project);
        File.WriteAllText(project, text.Replace("\"Reference\": \"Artist - Song (Me) [Hard].osu\"", "\"Reference\": \"../../evil.osu\""));
        Assert.Throws<FormatException>(() => KeysoundStore.Open(project));
        File.WriteAllText(project, "{ not json");
        Assert.Throws<FormatException>(() => KeysoundStore.Open(project));
        Assert.Throws<FormatException>(() => KeysoundStore.Validate(new KeysoundProjectFile
        {
            Reference = "a.osu",
            Tracks = [KeyTrack.Create("x") with { Notes = [new KeyNote(Guid.NewGuid(), 0, 0, 60, 100)] }],
        }));
        Assert.Throws<FormatException>(() => KeysoundStore.Validate(new KeysoundProjectFile
        {
            Reference = "a.osu",
            Tracks = [KeyTrack.Create("x") with { Instrument = new KeyInstrument(KeyInstrument.Vst3) }], // a plug-in with no path
        }));
    }

    [Fact]
    public void Suggested_folders_never_reuse_an_existing_one()
    {
        var first = KeysoundStore.SuggestFolder(root, @"C:\x\A: B [C].osu");
        Assert.Equal("A_ B [C]", Path.GetFileName(first));
        Directory.CreateDirectory(first);
        Assert.Equal("A_ B [C] (2)", Path.GetFileName(KeysoundStore.SuggestFolder(root, @"C:\x\A: B [C].osu")));
    }
}

public class KeyEditingTests
{
    private static KeyNote N(long start, long length, int pitch = 60) => new(Guid.NewGuid(), start, length, pitch, 100);

    [Fact]
    public void Split_cuts_only_notes_that_contain_the_tick_and_keeps_the_left_id()
    {
        var inside = N(0, 960);
        var edge = N(480, 480); // starts exactly at the tick: untouched
        var (changed, added) = KeyEditing.Split([inside, edge], 480);
        var left = Assert.Single(changed);
        var right = Assert.Single(added);
        Assert.Equal((inside.Id, 0L, 480L), (left.Id, left.Start, left.Length));
        Assert.Equal((480L, 480L, 60, 100), (right.Start, right.Length, right.Pitch, right.Velocity));
        Assert.NotEqual(inside.Id, right.Id);
    }

    [Fact]
    public void Glue_joins_with_the_next_note_of_the_same_pitch()
    {
        var a = N(0, 240);
        var other = N(300, 100, pitch: 62);
        var b = N(480, 480);
        var later = N(2000, 100);
        var (joined, removed) = KeyEditing.Glue([later, b, other, a], a)!.Value;
        Assert.Equal(b.Id, removed.Id);
        Assert.Equal((a.Id, 0L, 960L), (joined.Id, joined.Start, joined.Length));
        Assert.Null(KeyEditing.Glue([a, later], later));
    }

    [Fact]
    public void Duplicate_places_copies_after_the_selection_and_paste_keeps_spacing()
    {
        var notes = new[] { N(960, 480), N(1440, 960, 64) };
        var copies = KeyEditing.Duplicate(notes);
        Assert.Equal([2400L, 2880L], copies.Select(c => c.Start));
        Assert.DoesNotContain(copies, c => notes.Any(n => n.Id == c.Id));

        var pasted = KeyEditing.PasteAt(notes, 100);
        Assert.Equal([100L, 580L], pasted.Select(c => c.Start));
    }

    [Fact]
    public void Quantize_moves_starts_onto_the_grid()
    {
        var notes = new[] { N(250, 100), N(700, 100) };
        var q = KeyEditing.Quantize(notes, t => (long)Math.Round(t / 480.0) * 480);
        Assert.Equal([480L, 480L], q.Select(n => n.Start));
        Assert.Equal([100L, 100L], q.Select(n => n.Length));
    }

    [Fact]
    public void Neighbours_follow_time_then_pitch_and_following_of_pitch_selects_later_notes()
    {
        var a = N(0, 10, 60); var b = N(0, 10, 64); var c = N(480, 10, 60);
        Assert.Same(a, KeyEditing.Neighbour([c, b, a], null, 1));
        Assert.Same(b, KeyEditing.Neighbour([c, b, a], a, 1));
        Assert.Same(c, KeyEditing.Neighbour([c, b, a], b, 1));
        Assert.Null(KeyEditing.Neighbour([c, b, a], c, 1));
        Assert.Same(a, KeyEditing.Neighbour([c, b, a], b, -1));
        Assert.Equal([a.Id, c.Id], KeyEditing.FollowingOfPitch([a, b, c], a).Select(n => n.Id));
    }

    [Fact]
    public void Resizing_from_the_start_keeps_the_end_and_never_flips_a_short_note()
    {
        var normal = N(960, 960);
        var shortNote = N(100, 120); // shorter than the 240-tick minimum
        var resized = KeyEditing.ResizeStart([normal, shortNote], 480, minLength: 240);
        Assert.Equal((1440L, 480L), (resized[0].Start, resized[0].Length));
        Assert.Equal((100L, 120L), (resized[1].Start, resized[1].Length)); // unchanged, not pushed backwards
        var longer = KeyEditing.ResizeStart([normal], -480, minLength: 240)[0];
        Assert.Equal((480L, 1440L), (longer.Start, longer.Length));
    }

    [Fact]
    public void Replace_notes_is_one_undo_step()
    {
        var track = KeyTrack.Create("Piano");
        var s = new KeysoundSession(new KeysoundState([track]));
        var a = N(0, 960);
        s.AddNotes(track.Id, [a]);
        var (changed, added) = KeyEditing.Split([a], 480);
        s.ReplaceNotes(track.Id, new HashSet<Guid>(), changed, added, "Split");
        Assert.Equal(2, s.State.Track(track.Id)!.Notes.Count);
        s.Undo();
        Assert.Equal(a, Assert.Single(s.State.Track(track.Id)!.Notes));
    }
}
