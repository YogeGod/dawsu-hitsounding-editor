# osu!standard hitsound encoding rules (for a lossless hitsound editor)

Researched 2026-10-29 against primary sources only. Source code is pinned to a commit so line numbers stay valid:

- **osu!lazer**: `ppy/osu` at commit `b77b028f9a0256ce23b94ef7eb916648641553e7`. Link prefix used below: `https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/`
- **osu-wiki**: `ppy/osu-wiki` at commit `e229a8cf43d5a86d70d4802f3427083137c186b3`. Link prefix used below: `https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/`
- **osu!stable source** (`peppy/osu-stable-reference`, which lazer comments link to) is **not public** (it returned 404 when fetched). So anything about stable comes either from the wiki or from lazer comments that quote stable. Stable-only behaviour that neither source states is marked **UNVERIFIED**.

Short names for files (each name links to its pinned file; `#Lx-Ly` anchors give the line numbers):

| Key | File |
|---|---|
| CHP | [osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs) |
| LBD | [osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs) |
| LD | [osu.Game/Beatmaps/Formats/LegacyDecoder.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs) |
| LBE | [osu.Game/Beatmaps/Formats/LegacyBeatmapEncoder.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapEncoder.cs) |
| HSI | [osu.Game/Audio/HitSampleInfo.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Audio/HitSampleInfo.cs) |
| SCP | [osu.Game/Beatmaps/ControlPoints/SampleControlPoint.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/SampleControlPoint.cs) |
| LCPI | [osu.Game/Beatmaps/Legacy/LegacyControlPointInfo.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacyControlPointInfo.cs) |
| CPI | [osu.Game/Beatmaps/ControlPoints/ControlPointInfo.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/ControlPointInfo.cs) |
| LS | [osu.Game/Skinning/LegacySkin.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacySkin.cs) |
| LBS | [osu.Game/Skinning/LegacyBeatmapSkin.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacyBeatmapSkin.cs) |
| BSPC | [osu.Game/Skinning/BeatmapSkinProvidingContainer.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/BeatmapSkinProvidingContainer.cs) |
| SKIN | [osu.Game/Skinning/Skin.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/Skin.cs) |
| SS | [osu.Game/Skinning/SkinnableSound.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/SkinnableSound.cs) |
| DHO | [osu.Game/Rulesets/Objects/Drawables/DrawableHitObject.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Drawables/DrawableHitObject.cs) |
| HO | [osu.Game/Rulesets/Objects/HitObject.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/HitObject.cs) |
| SLD | [osu.Game.Rulesets.Osu/Objects/Slider.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Slider.cs) |
| SPN | [osu.Game.Rulesets.Osu/Objects/Spinner.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Spinner.cs) |
| DSL | [osu.Game.Rulesets.Osu/Objects/Drawables/DrawableSlider.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Drawables/DrawableSlider.cs) |
| SEG | [osu.Game/Rulesets/Objects/SliderEventGenerator.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/SliderEventGenerator.cs) |
| LSBD | [osu.Game/Beatmaps/Formats/LegacyStoryboardDecoder.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyStoryboardDecoder.cs) |
| ENUMS | [LegacySampleBank.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacySampleBank.cs), [LegacyHitSoundType.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacyHitSoundType.cs), [LegacyEventType.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacyEventType.cs) |
| WIKI-OSU | [Client/File_formats/osu_(file_format)/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Client/File_formats/osu_%28file_format%29/en.md) (live: https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29) |
| WIKI-SBA | [Storyboard/Scripting/Audio/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Storyboard/Scripting/Audio/en.md) |
| WIKI-SKS | [Skinning/Sounds/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Skinning/Sounds/en.md) |
| WIKI-HS | [Beatmapping/Hitsound/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Beatmapping/Hitsound/en.md) (the page `Gameplay/Hitsound` does not exist; the hitsound article is at `Beatmapping/Hitsound`) |
| WIKI-UCH | [Guides/Using_custom_hitsounds/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Guides/Using_custom_hitsounds/en.md) |
| WIKI-CSO | [Guides/Custom_sample_overrides/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Guides/Custom_sample_overrides/en.md) |
| WIKI-RC | [Ranking_criteria/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Ranking_criteria/en.md) (live: https://osu.ppy.sh/wiki/en/Ranking_criteria) |
| WIKI-RCO | [Ranking_criteria/osu!/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Ranking_criteria/osu%21/en.md) |
| WIKI-VAR | [Storyboard/Scripting/Variables/en.md](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Storyboard/Scripting/Variables/en.md) |

---

## TL;DR: rules that matter for the compiler and decompiler

1. **The hitnormal sound is always present.** `hitSound` bits are Normal=1, Whistle=2, Finish=4, Clap=8. A value of 0 means "normal only". If additions are set without bit 0, lazer still adds a *layered* hitnormal, which a skin can turn off with `LayeredHitSounds: 0`. If bit 0 is set, the hitnormal is never layered and always plays. Sources: WIKI-OSU §Hitsounds; [CHP L543-569](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L543-L569).
2. **hitSample is `normalSet:additionSet:index:volume:filename`.** A 0 in any of the first four fields means "inherit". normalSet 0 inherits the timing point's set. additionSet 0 inherits the *normal* sound's resolved set. index 0 inherits the timing point's index. volume 0 inherits the timing point's volume. Sources: WIKI-OSU §Custom hit samples; [CHP L197-238](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L197-L238); [LD L192-204](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L192-L204).
3. **The effective index decides where the file comes from.**
   - Index 0: skin or default only. The beatmap folder is never read.
   - Index 1: `<set>-hit<sound>.wav` from the beatmap folder, with **no** "1" suffix (`soft-hitclap1.wav` is never used).
   - Index N≥2: `<set>-hit<sound>N.wav` from the beatmap folder.
   - If a custom file is missing, playback falls through to the user skin's *unsuffixed* sample, never to the beatmap's index-1 file.
   - Sources: WIKI-OSU; WIKI-UCH; [HSI L89-100](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Audio/HitSampleInfo.cs#L89-L100); [CHP L639-650](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L639-L650); [LS L610-634](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacySkin.cs#L610-L634); [LBS L92-98](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacyBeatmapSkin.cs#L92-L98).
4. **Extension order is wav, then mp3, then ogg** (lazer copies this order from stable). Source: [SKIN L176-189](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/Skin.cs#L176-L189).
5. **The timing point is looked up at object time + 5 ms** (`CONTROL_POINT_LENIENCY = 5`, which lazer copies from stable). A green line placed up to 5 ms *after* a circle still applies to it. The exact lookup times differ by part:
   - Slider body: start + 6 ms.
   - Slider head, repeats and tail: each node's own time + 5 ms.
   - Spinner: **end** time + 5 ms.
   - Source: [LBD L32-38, L168-190](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L168-L190).
6. **The custom filename field is where stable and lazer disagree most.**
   - The wiki says that with a filename, "no addition sounds will be played, and this file … is played instead". It also calls the field the "filename of the addition sound".
   - In lazer the file **replaces only the hitnormal**, and whistle, finish and clap **still play** ([CHP L547-566](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L547-L566)).
   - **Safe encoding:** use a filename only on objects whose `hitSound` is 0 (or 1), so no additions are set.
   - Lazer **ignores** index, volume and filename in a *slider's* own hitSample. For sliders it reads only the two banks ([CHP L105-106, L229](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L105-L106)).
7. **Volume is an override, not a product.** The hitSample volume is used if it is above 0, otherwise the timing point's volume. Timing-point volume is clamped to 0–100. Lazer never plays a sample below 5% (stable's floor is 8%). Sources: [LD L198](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L198); [SCP L44-48](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/SampleControlPoint.cs#L44-L48); [DHO L168-186](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Drawables/DrawableHitObject.cs#L168-L186).
8. **Never use lazer's encoder as the reference for round-trips.** It rewrites the header to `v128` and regenerates `[TimingPoints]` from the hit-object samples. Outside mania it also zeroes per-object index and volume, and it drops sections and keys. A lossless tool must keep raw lines. Source: [LBE L24-80, L182-397, L593-624](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapEncoder.cs#L593-L624).
9. **Sample files are shared across the whole mapset folder.** Custom-index files are used by every difficulty whose timing points or objects point at that index. Files named in hitSample filename fields or storyboard `Sample` lines (including the shared `.osb`) are also shared. Before changing or deleting any sample file, scan *all* `.osu` files and the `.osb`.
10. **Ranking criteria constraints on sample files:**
    - Every hitsound file must be at least 25 ms long.
    - Silent files must be the official 44-byte `blank.wav`.
    - There must be no unused files and no 0-byte files.
    - Active hitsounds must be wav or ogg, with a peak delayed by no more than 5 ms.
    - Looping samples (`sliderslide`, `sliderwhistle`, `spinnerspin`) should loop naturally.
    - Hitsounds must be made by direct human input, with no generative tooling.
    - Sources: WIKI-RC §Audio, §AI policy; WIKI-RCO.

---

## 1. HitObject `hitSound` and `hitSample`

**hitSound bit flags**
- Bit 0 = Normal, 1 = Whistle, 2 = Finish, 3 = Clap ([WIKI-OSU §Hitsounds](https://github.com/ppy/osu-wiki/blob/e229a8cf43d5a86d70d4802f3427083137c186b3/wiki/Client/File_formats/osu_%28file_format%29/en.md); [ENUMS LegacyHitSoundType](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacyHitSoundType.cs)).
- Wiki: "If no bits are set, the normal hitsound is used by default", and "In every mode except osu!mania, the `LayeredHitSounds` skin property forces the normal sound to be included regardless of bit 0's setting. It is enabled by default."
- Lazer always emits a `hitnormal` sample, or the filename sample instead ([CHP L547-557](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L547-L557)). That sample is `IsLayered = type != None && !type.HasFlag(Normal)`.
  - So `hitSound=0` gives a non-layered hitnormal.
  - `hitSound=2` gives a layered hitnormal plus a whistle.
  - `hitSound=3` gives a non-layered hitnormal plus a whistle.
  - A skin with `LayeredHitSounds: 0` can mute only *layered* hitnormals ([CHP L625-631](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L625-L631)).
- Additions are added for each set bit, in this order: finish, whistle, clap ([CHP L559-566](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L559-L566)).

**hitSample `normalSet:additionSet:index:volume:filename`** (WIKI-OSU §Custom hit samples)
- **Sets:** 0 means none or inherit, 1 = normal, 2 = soft, 3 = drum ([ENUMS LegacySampleBank](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacySampleBank.cs)).
  - Wiki: for normalSet 0, "the set is determined by the timing point's sample set". For additionSet 0, "the set is determined by the normal sound's sample set".
  - Lazer: `BankForAdditions = additionSet==None ? BankForNormal : additionSet`. If the result is null (both are 0), the sample's `BankSpecified=false` and the timing point's bank is used ([CHP L212-227, L634-648](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L212-L227); [LD L199](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L199)).
  - Out-of-range object set values (for example 4) are coerced to Normal in lazer ([CHP L204-210](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L204-L210)). Stable behaviour for these is UNVERIFIED.
- **index:** wiki says "If this is `0`, the timing point's sample index will be used instead." Lazer: `legacy.CustomSampleBank > 0 ? legacy.CustomSampleBank : timingPoint.CustomSampleBank` ([LD L197](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L197)). Negative values are therefore treated like 0, which inherits.
- **volume:** wiki says "from 1 to 100. If this is `0`, the timing point's volume will be used instead." Lazer parses it as `Math.Max(0, int)` with **no upper clamp** ([CHP L234-235](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L234-L235)). What happens to values over 100 at playback is UNVERIFIED.
- **filename:** lazer takes `split[4]` if present ([CHP L237](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L237)). A filename that contains `:` is therefore truncated in lazer.
  - A non-empty filename creates `FileHitSampleInfo` *in place of* the hitnormal. Its lookups are `Filename`, then `Filename` without extension, then the normal `normal-hitnormal` / `hitnormal` chain, with `customSampleBank: 1` so beatmap lookup is allowed ([CHP L685-700](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L685-L700)).
  - Its bank is always `normal`, because `With()` ignores bank changes ([CHP L702-704](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L702-L704)). Volume still inherits from the timing point when it is 0.
  - **Conflict:** the wiki says that with a filename "no addition sounds will be played, and this file in the beatmap directory is played instead". Lazer still plays the additions, because `convertSoundType` appends finish, whistle and clap regardless. Whether stable *also* drops the hitnormal is ambiguous in the wiki, which calls the field the "filename of the addition sound". **UNVERIFIED for stable; test it empirically.**
- **When hitSample may be omitted:** wiki says "If it is not written, it defaults to `0:0:0:0:`". Lazer reads it only if the field exists: circle `split.Length>5`, slider `>10`, spinner `>6` ([CHP L83-84, L105-106, L164-165](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L83-L84)). Partial forms such as `1:0` or `1:0:0` are accepted: missing index and volume stay 0, and a missing filename becomes null.

## 2. Sliders

Syntax: `x,y,time,type,hitSound,curve,slides,length,edgeSounds,edgeSets,hitSample` (WIKI-OSU §Sliders).

- **Nodes.** A slider has `slides + 1` nodes: the head, each repeat, and the tail.
  - **edgeSounds** is a `|`-separated list of hitSound integers, one per node.
  - **edgeSets** is a `|`-separated list of `normalSet:additionSet`, one per node.
  - In lazer every node starts as a copy of the slider's own `hitSound` and bank info. edgeSounds and edgeSets then override per node, up to the length of each list. Missing entries keep the slider-level values ([CHP L108-154](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L108-L154)).
  - Node edgeSets are parsed with the full `readCustomSampleBanks`, not banks-only. In lazer a node entry like `1:2:3:50:x.wav` would therefore set index, volume and filename. Stable writes only `n:a` (WIKI-OSU), so this has no defined meaning in stable (UNVERIFIED).
- **Slider-level hitSample in lazer is banks-only.** `readCustomSampleBanks(split[10], bankInfo, true)` returns early, before index, volume and filename are read ([CHP L105-106, L229](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L229)). So in lazer **a slider's index, volume and filename always come from timing points.** Whether stable honours a slider's hitSample index, volume or filename is UNVERIFIED. **Editor rule:** control slider index and volume only through green lines.
- **Slider body sound** (WIKI-OSU §Slider hitsounds): "only the normal and whistle sounds are supported". The filename is `<sampleSet>-slider<slide|whistle><index>.wav`, and the sound loops while the player tracks the slider.
  - Lazer builds `sliderslide` from the slider-level hitnormal, and `sliderwhistle` only if the slider-level `hitSound` has Whistle ([HO L205-218](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/HitObject.cs#L205-L218); [DSL L123-128, L168](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Drawables/DrawableSlider.cs#L123-L128)).
  - Finish and clap on the slider body play nothing continuous.
- **Slider ticks.** In lazer, ticks use `slidertick` derived from the slider-level hitnormal (or the first sample) ([SLD L256-271](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Slider.cs#L256-L271)). So ticks share the **body's bank, index and volume, which are evaluated once at slider start + 6 ms**, not at each tick's time.
  - Whether stable evaluates ticks at their own time is UNVERIFIED.
  - Tick generation: [SEG](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/SliderEventGenerator.cs) (tick distance = scoring distance / `SliderTickRate`; no tick within `velocity*10` of a span end). An inherited point with NaN beatLength disables ticks ([LBD L490-494, L548](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L490-L494)).
- **When each part is evaluated** ([LBD L168-183](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L168-L183)):
  - The body (slide, whistle and ticks) uses the sample point at `StartTime + 5 + 1`.
  - Node *i* uses the sample point at `StartTime + i*Duration/SpanCount + 5`.
  - So the head, every repeat and the tail can each pick up a different green line (bank, index and volume).
- **Node times** need the slider duration. One slide takes `length / (SliderMultiplier*100*SV) * beatLength` ms (WIKI-OSU notice). Lazer clamps the SV-as-beatLength to 10–1000 for osu! ([LegacyRulesetExtensions.GetPrecisionAdjustedBeatLength](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/LegacyRulesetExtensions.cs)), which means SV 0.1–10.
- **Who plays what in lazer:** head and repeat samples belong to the nested objects, and tail samples are played by the slider at its end ([SLD L273-286](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Slider.cs#L273-L286)).
- **Zero-length sliders:** lazer drops their repeats and keeps only the first and last node samples, which deliberately does not match stable ([CHP L484-498](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L484-L498)).

## 3. Spinners

Syntax: `x,y,time,type,hitSound,endTime,hitSample` (WIKI-OSU §Spinners).

- The spinner's `hitSound` plus hitSample is a normal hit sample set (hitnormal plus additions). In lazer it is resolved at **endTime + 5 ms** (non-repeat branch of [LBD L184-189](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L184-L189)) and played at spinner completion. WIKI-HS calls the spinner end a "passive hitsound".
- `spinnerspin` (looped and pitch-modulated) is derived from the spinner's first sample, so it takes that sample's bank, index and volume ([SPN L104-116](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Spinner.cs#L104-L116)).
- `spinnerbonus` is played on each bonus tick. It is created by `CreateHitSampleInfo("spinnerbonus")`, which uses the first addition's settings if there is one, otherwise the hitnormal's ([SPN L94](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Spinner.cs#L94); [HO L229-243](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/HitObject.cs#L229-L243)).
- `spinnerbonus-max` exists in lazer only (WIKI-CSO).
- The wiki lists `spinnerspin` and `spinnerbonus` as **universal** samples, with no set prefix (WIKI-CSO, WIKI-SKS).
- **Lazer lookup for these:** `Gameplay/<bank>-spinnerspin[N]`, then `<bank>-spinnerspin`, then `spinnerspin` ([HSI L89-100](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Audio/HitSampleInfo.cs#L89-L100)). So a beatmap file `soft-spinnerspin.wav` works in lazer. Whether stable supports set-prefixed or indexed spinner samples is UNVERIFIED.

## 4. Timing points

Syntax: `time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects` (WIKI-OSU §Timing points).

- **sampleSet:** the wiki defines it as "0 = beatmap default, 1 = normal, 2 = soft, 3 = drum".
  - **Conflict:** lazer maps a parsed sampleSet of `0` (None) to `normal`, **not** to `[General] SampleSet` ([LBD L526-528](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L526-L528)).
  - In lazer, `[General] SampleSet` (and the legacy `[General] SampleVolume`) are used only when the timing-point line has **fewer than 4 (or 6) fields** ([LBD L265-271, L500-510](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L500-L510)). What stable does with sampleSet 0 is UNVERIFIED.
  - Default for `[General] SampleSet`: `Normal` (WIKI-OSU §General).
- **sampleIndex:** wiki says "`0` indicates osu!'s default hitsounds". It is an int with no upper limit in the file. The stable editor natively offers indices 2–100, and higher values need hand editing (WIKI-UCH).
- **volume:** a percentage. Lazer clamps it to 0–100 in `SampleVolumeBindable` ([SCP L44-48](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/SampleControlPoint.cs#L44-L48)).
- **Inherited and uninherited points:** both carry sampleSet, index and volume. Both kinds are sample points in lazer ([LBD L563-568](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L563-L568)).
- **Two points at the same time:** the non-timing (green) point's sample settings override the red point's, whatever their order in the file. If several green lines share a time, the last one in the file wins ([LBD L576-603](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L576-L603)). Ranking forbids two red or two green points at the same time (WIKI-RC §Timing rules).
- **Choosing the active point:** it is the rightmost sample point with `point.time <= queryTime` ([CPI L246-264](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/ControlPointInfo.cs#L246-L264)).
  - Before the first point, lazer uses the **first** sample point ([LCPI L26](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacyControlPointInfo.cs#L26)).
  - With no points at all it uses DEFAULT (normal, 100%).
  - `queryTime` is object time + `CONTROL_POINT_LENIENCY` (5 ms). Lazer documents this as copied from stable `HitObject.cs#L319` ([LBD L32-38](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L32-L38)); see §2 and §3 for the per-part offsets.
- **Timing offset for old files:** for v3/v4 files (`FormatVersion < 5`), lazer adds +24 ms to all parsed times ([LBD L28-30, L75](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L28-L30)). The lane view should apply this when aligning to audio, but must **write back the original numbers**.
- **Number formats:** `time` and `beatLength` are Decimal, and `meter`, `sampleSet`, `sampleIndex`, `volume` and `effects` are Integer (WIKI-OSU). Lazer parses the time as a double ([LBD L488](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L488)). Fractional red and green times exist in real files, so the object-vs-point comparison must use doubles.

## 5. Sample file naming and lookup

- **Name pattern:** `<set>-hit<normal|whistle|finish|clap><index>.wav`. The index "is not written if the value is `0` or `1`" (WIKI-OSU).
- **Load order** (WIKI-OSU): "Beatmap, if `index` is not `0`" → "Skin, with the `index` removed" → "Default osu! resources, with the `index` removed".
- WIKI-UCH confirms that `soft-hitclap1.wav` "won't work, and `soft-hitclap.wav` will be used instead". Custom index N≥2 needs a green or red line with that index, unless the object sets the index itself.
- **Slider and universal names:** `<set>-sliderslide`, `<set>-sliderwhistle`, `<set>-slidertick` (WIKI-CSO, WIKI-SKS). For slider body sounds the pattern is `<set>-slider<…><index>` (WIKI-OSU).
- **Extensions:**
  - The skinning wiki says "`.wav`, `.mp3`, and/or `.ogg` are valid formats for sounds" (WIKI-SKS).
  - The storyboard audio wiki lists WAV, MP3 and OGG (WIKI-SBA).
  - Lazer looks them up in the order wav → mp3 → ogg, "osu-stable performs audio lookups in order of wav -> mp3 -> ogg" ([SKIN L176-189](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/Skin.cs#L176-L189)).
  - **Implication:** if both `soft-hitclap2.wav` and `soft-hitclap2.ogg` exist, the `.wav` wins, and the `.ogg` is an unused file under the ranking criteria.
- **Lazer lookup in detail:**
  - `HitSampleInfo.LookupNames` = `Gameplay/{bank}-{name}{suffix}` (only if a suffix exists), then `Gameplay/{bank}-{name}`, then `Gameplay/{name}` ([HSI L89-100](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Audio/HitSampleInfo.cs#L89-L100)).
  - `suffix` = index as a string only when index ≥ 2. `UseBeatmapSamples` = index ≥ 1 ([CHP L639-650](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L639-L650)).
  - `LegacySkin.getLegacyLookupNames` also tries each name without the `Gameplay/` prefix. When there is a suffix:
    - The **beatmap skin** (`UseCustomSampleBanks=true`) keeps **only** the suffixed names.
    - A **user skin** (`false`) keeps **only** the unsuffixed names.
    - Both then fall back to the bare `{name}` (for example `hitclap`) "as the last resort" ([LS L41-44, L610-634](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacySkin.cs#L610-L634); [LBS L22](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacyBeatmapSkin.cs#L22)).
  - The beatmap skin returns nothing for samples with `UseBeatmapSamples=false`, that is index 0 ([LBS L92-98](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/LegacyBeatmapSkin.cs#L92-L98)).
  - **Lazer-only quirk:** a beatmap-folder file named bare `hitclap.wav` (no set prefix) satisfies custom-index lookups in lazer. Whether stable does this is UNVERIFIED.
- **Missing custom file:** if a custom-index file is missing (for example `soft-hitclap3`), the beatmap lookup fails and the **user skin's** `soft-hitclap` is used. It does **not** fall back to the beatmap's `soft-hitclap.wav` (index 1). This agrees with the wiki ("Skin, with the index removed"). The decompiler must model this as "lane = skin sample", not as a beatmap file.
- **Missing filename-override file:** lazer falls back to `normal-hitnormal` / `hitnormal` (beatmap first, because customSampleBank is 1, then skin) ([CHP L689-700](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L689-L700)). Stable behaviour is UNVERIFIED.
- **"Beatmap hitsounds" setting** (lazer): the beatmap skin's sample lookup is allowed only when `BeatmapHitsounds` is true. **Storyboard samples are always allowed** ([BSPC L16-28](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/BeatmapSkinProvidingContainer.cs#L16-L28)). With beatmap hitsounds off, every custom-index sound collapses to the user skin's sample. Stable's equivalent option ("Ignore beatmap hitsounds") is UNVERIFIED in detail.
- **Skin chain** (lazer): beatmap skin → user skin(s) → classic or default fallbacks, with ruleset resources inserted before the game-wide default skins ([RulesetSkinProvidingContainer.cs](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/RulesetSkinProvidingContainer.cs)).
- **Filename case-sensitivity** of lookups (beatmap files on Linux/macOS lazer, stable on Windows) is UNVERIFIED. Preserve the exact case and avoid creating names that differ only in case.

## 6. Does hitnormal always play? What does "auto" mean?

- **Yes, except as noted:** see §1. Every object has a hitnormal sample. When additions are set without bit 0, that hitnormal is "layered" and can be suppressed by `LayeredHitSounds: 0` in the user's skin (WIKI-OSU; [CHP L549-552](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L549-L552)).
- To make a sample "silent", mappers point the hitnormal at a silent custom file. Ranking requires that silent file to be the official 44-byte `blank.wav` (WIKI-RC).
- **"Auto" addition set:** additionSet 0 means the same set as the resolved normal set (WIKI-OSU; [CHP L227](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L227)).
- **Lazer's EditorAutoBank flag:** in lazer, additionSet 0 also sets `EditorAutoBank=true`, which affects editor behaviour only ([CHP L218-224](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L218-L224)).

## 7. Volume

- **Per sample:** `effective = hitSample.volume > 0 ? hitSample.volume : timingPoint.volume` ([LD L198](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L198); [SCP L77-78](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/SampleControlPoint.cs#L77-L78)). This is an override, not a multiplication.
- **Applies to the whole object:** one volume covers every sample of an object or node, including the hitnormal, the additions and the filename sample. There is no per-addition volume in the format.
- **Timing-point range:** 0–100, clamped in lazer ([SCP L44-48](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/ControlPoints/SampleControlPoint.cs#L44-L48)).
- **Hit-object range:** the wiki gives 1–100, with 0 meaning inherit. Lazer clamps only negative values.
- **Volume floor:** lazer plays at `max(volume, 5)/100` ([SS L168](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Skinning/SkinnableSound.cs#L168); [DHO L168-186](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Drawables/DrawableHitObject.cs#L168-L186)). The DHO comment says stable's floor is **8%**, applied silently, while the stable editor's minimum selectable value is 5%. **Volume 0 on a timing point therefore does not mute** (5% in lazer, 8% in stable).
- **Looped slider samples** also use `MinimumSampleVolume` ([DSL L123-128](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game.Rulesets.Osu/Objects/Drawables/DrawableSlider.cs#L123-L128)).
- **Ranking:** "Hitsounds must be audible", and low-volume or blending samples are unacceptable (WIKI-RC).

## 8. Storyboard samples

- **Syntax:** `Sample,<time>,<layer_num>,"<filepath>",<volume>`. Layers are 0 = Background, 1 = Fail, 2 = Pass, 3 = Foreground. Volume is 1–100 and is optional (default 100). Formats are WAV, MP3 and OGG (WIKI-SBA).
- **Allowed files:** sample lines may appear in `[Events]` of an `.osu` (difficulty-specific) or in the `.osb` (shared by all difficulties) (WIKI-OSU §Storyboards).
- **Lazer parsing:**
  - Event type `Sample` or `5` ([ENUMS LegacyEventType](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Legacy/LegacyEventType.cs)).
  - Time is parsed as a double.
  - The path goes through `CleanFilename`, which strips quotes, turns `\\` into `\` and standardises separators ([LD L166-170](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L166-L170)).
  - Volume is parsed as a float and cast to int ([LSBD L186-193](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyStoryboardDecoder.cs#L186-L193)).
- **Variables:** `.osb` files may use a `[Variables]` section with `$name=value` substitution. This is not supported in `.osu` (WIKI-VAR). A tool must substitute variables to *read* sample paths, but write back the raw, unsubstituted text.
- **Ranking:** storyboarded hitsounds cannot replace active hitsounds, and "Refrain from usage of storyboard sound samples in ways that are easily confused with hitsounds" (WIKI-RC §Audio, §Storyboarding). osu!mania is exempt from the first rule.

## 9. Pitfalls for a lossless round-trip

**What lazer's parser tolerates.** A lossless tool must keep all of the following byte-exact; lazer reads them but its encoder would change them.
- **BOM:** lazer reads with `StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true)`, so a UTF-8 BOM is silently consumed ([LineBufferedReader.cs L22](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/IO/LineBufferedReader.cs#L22)). Keep the original leading bytes.
- **Leading blank lines** before `osu file format vN` are skipped, and the header is matched by prefix ([Decoder.cs L62-75](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/Decoder.cs#L62-L75)). Files without a header fall back to the latest decoder ([LBD L53-57](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L53-L57)).
- **Comments and whitespace** ([LD L38-108](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L38-L108)):
  - Whole-line `//` comments are skipped.
  - In every section except `[Metadata]`, anything from a `//` at index > 0 to the end of the line is stripped.
  - Trailing whitespace is trimmed.
  - Unknown section names are logged, not fatal.
  - Lines that fail to parse are logged and skipped.
- **Line endings:** stable is commonly assumed to write CRLF, but this is UNVERIFIED from primary sources. Detect each file's line-ending style and preserve it, including mixed endings and a missing final newline.
- **Numbers:** invariant-culture parsing. Hit-object `time` is "Integer" in the wiki, but lazer parses it as a double ([CHP L64](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L64)). Timing-point time and beatLength are decimals (for example `333.33`, or long doubles from the editor). **Never re-format a number you did not change.**
- **Positions:** for format versions below 128, positions are parsed as float and truncated to int ([CHP L57-62](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs#L57-L62)).
- **hitSample variants seen in the format:** absent; `0:0:0:0:` (trailing colon, empty filename, which is the wiki default); `0:0:0:0` with no trailing colon; shorter `n:a` forms. These are semantically equal but byte-distinct. Preserve the original form; when writing new data, emit the wiki's canonical `n:a:i:v:` with a trailing colon.
- **Equivalent hitSound values:** `0` and `1` both play just the hitnormal, but they differ in layering (§1). Preserve which one the file used.

**Format versions:**
- Header `osu file format vN`. Stable's latest is v14 and lazer writes v128 (`FIRST_LAZER_VERSION`) (WIKI-OSU §Structure; [LD L21](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyDecoder.cs#L21); [LBE L24](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapEncoder.cs#L24)).
- Version-gated behaviour found in lazer:
  - v<5: +24 ms offset.
  - v<6: storyboard animation frame-delay quirk ([LSBD L176-178](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyStoryboardDecoder.cs#L176-L178)).
  - v<128: integer positions.
- Older files may lack trailing fields: hitSample, edgeSounds, edgeSets, and timing-point fields 3–8. Lazer defaults them (timing points: [LBD L496-524](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs#L496-L524)).
- A full per-version field history for v3–v14 is **UNVERIFIED** (no public stable source).
- **Editor rule:** when *adding* fields to an old-format line (for example giving a v5 circle a hitSample), check that the version supports it, or refuse.

**What lazer's encoder normalises.** A lossless tool must **not** do any of these ([LBE](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Beatmaps/Formats/LegacyBeatmapEncoder.cs)):
- Rewrites the header to `v128`. It writes sections in a fixed order with a fixed key set, and drops unknown keys and sections, comments, and `[Variables]` (L55-160).
- Writes `SampleSet` from the first sample point (L92-93).
- **Regenerates `[TimingPoints]`.** It extracts a sample control point per object or node from the hit-object samples, then drops redundant ones (L182-267, L334-397). It also strips kiai from red lines (L240-253).
- Writes object hitSample with **index=0 and volume=0 for non-mania** ("already satisfied by the control points"), and writes the filename from `LookupNames.First()` (L593-624).
- Writes the slider edge banks as `n:a` only, and recomputes the number of edge entries (L560-575).
- Re-formats all numbers (invariant `ToString` of doubles and floats).

**Recommendations:**
- Model each file as an ordered list of **raw lines**, grouped by section, with each line's original terminator.
- Parse a semantic view alongside the raw lines. On export, only re-serialise lines whose semantic value changed, and serialise *only the changed field* by patching the raw token. Everything else is emitted verbatim.
- Hit-object identity is the line index, plus a hash of the raw line for conflict detection.
- Test: import → export with no edits must be byte-identical. Run it on a corpus that includes a BOM, LF-only and CRLF files, v3/v5/v14/v128, comments, missing hitSample, `0:0:0:0` without a trailing colon, and decimal timing points.

## 10. Ranking-criteria constraints relevant to the tool

All from WIKI-RC §Audio, §General, §AI policy and WIKI-RCO unless noted.
- **No unused files and no 0-byte files** in the folder (WIKI-RC general rules). This applies to the hitsound-diff workflow: unused samples and the helper diff must be removed before upload. Export must not leave orphaned sample files.
- "Every hitsound file must be at least 25ms long."
- "Completely silent sound files must use this 44-byte file" (https://assets.ppy.sh/media/blank.wav).
- Every clicked part needs at least one hitsound with a clear impact, a peak at most 5 ms late, in WAV or OGG (not MP3). Skinning wiki: "Hitsounds should not have any more than a 5 millisecond delay" (WIKI-SKS).
- Beatmaps must be hitsounded. osu!standard: every actively clicked part needs at least one audible hitsound that does not blend with the song, and keysounds as hitnormals without other distinct additions are not allowed (WIKI-RCO).
- Guidelines:
  - Slider ends, reverses and spinner ends should have hitsound feedback.
  - Avoid `sliderslide`, `sliderwhistle` and `spinnerspin` samples that do not loop naturally (WIKI-RCO).
  - Avoid replacing the soft/normal hitfinish with frequently used custom samples.
  - Non-active gameplay sounds should be MP3 or OGG, unless WAV is smaller.
- Timing:
  - No two red or two green points at the same time.
  - No green line before the first red line.
  - The first red line must not toggle kiai (WIKI-RC timing rules). The tool must refuse, or warn, when an index or volume change would create such a point.
- **AI policy:** "A beatmap's hit objects, hitsounds and timing must be created exclusively by direct human input without the use of any generative tooling." A deterministic editor driven by the mapper is human input. Any auto-hitsounding feature (inferring hitsounds from audio) would conflict with this for ranked maps.
- **No rule found** on a total size or count limit for hitsound files, beyond the general rules above.

---

## Implications for the editor

### Lane model
A **lane** is a resolved playable source. Each lane is one of:
- `beatmap:<exact filename present in the folder>`: a custom-index file or a filename-override file.
- `skin:<set>-<sound>`: an index-0 sample, or a custom sample whose file is missing.

The lane key must use the extension that actually resolved (wav, then mp3, then ogg). Two difficulties may refer to the same lane.

### Decompile: which files play for an object, and how loud
```
input: object O (raw line + parsed), timing points TP (sorted, raw), folder file set F
for each "hit event" e of O:
   circle: e = {t=O.time, q=O.time+5,  sound=O.hitSound, banks/index/vol/filename = O.hitSample}
   spinner: e = {t=O.endTime, q=O.endTime+5, same fields}
   slider node i (0..slides): t_i = O.time + i*spanDur; q = t_i+5;
        sound = edgeSounds[i] ?? O.hitSound; banks = edgeSets[i] ?? O.hitSample banks;
        index/vol/filename: lazer -> from TP only (slider-level hitSample ignored, §2); stable UNVERIFIED
   slider body: q = O.time+6; sounds = sliderslide (always) + sliderwhistle (if O.hitSound&2)
        ticks: slidertick at each tick time, using the body's resolved bank/index/vol (lazer)
  P = active TP at q (rightmost time<=q; on ties a green line wins; before the first point use the first point)
  normalSet = e.normalSet!=0 ? e.normalSet : (P.sampleSet!=0 ? P.sampleSet : Normal   # lazer; wiki says [General] default — OPEN)
  addSet    = e.addSet!=0 ? e.addSet : normalSet
  index     = e.index>0 ? e.index : P.sampleIndex
  vol       = e.vol>0 ? e.vol : P.volume ; played = max(vol, 5 lazer / 8 stable)
  sounds = [normal] + [finish if &4] + [whistle if &2] + [clap if &8]
  if e.filename: replace normal by beatmap file e.filename (lazer: additions remain; stable: wiki says additions dropped — OPEN)
  resolve(set, sound, index):
     if index>=1 and BeatmapHitsounds:
        name = set-hit<sound> + (index>=2 ? index : "")
        if name.{wav,mp3,ogg} in F -> lane beatmap:that file
        (lazer only: bare "hit<sound>.*" in F also matches -> OPEN for stable)
     -> lane skin:set-hit<sound>   (layered normal: mark "skin-optional")
  emit (lane, time=e.t, volume=vol) for each sound
```

### Compile (export)
- **Apply mode** (objects are fixed): for each object, find the cheapest encoding that reproduces the lane set at that time.
  - Preferred: bits plus banks on the object, reusing an existing index that is already active at q.
  - Fallback 1: insert or modify a green line at time g ≤ t+5 (normally g = t). A green line at g affects every event whose q=t'+5 ≥ g, so it also catches earlier events within 5 ms before g. Pick g so that no earlier event lies in [g-5, t).
  - Fallback 2: the object's own index/volume field (circles and spinners only).
  - Last resort: the filename field, with `hitSound=0` and no additions.
  - Whenever a green line is inserted, the following object must see the old settings again. Insert a restoring green line right after the object, which raises the cost.
- **Constraints the compiler must respect:**
  - The lanes playable at one instant are limited to: one hitnormal file, plus up to three addition files that share *one* addition set and *one* index (they share index and volume with the hitnormal). So at most 4 distinct files per event, sharing one volume and one index.
  - Lane combinations needing different indices or volumes at the same instant cannot be encoded without a filename override or a second object. Generate mode can stack objects at the same time, but this conflicts with ranking criteria for a real difficulty. That is fine for a hitsound diff, which is not uploaded.
- **Sample-file safety:** before writing or overwriting `<set>-hit<sound><N>.<ext>`, build a usage index across **all** `.osu` files and the `.osb` in the folder. Only allocate an index N whose files are unused by other difficulties, or whose contents are byte-identical.
- **Minimal diff:** patch only the changed tokens of changed lines. Keep the original form of untouched lines, timing-point lines and hitSample strings.

---

## Open questions (need empirical tests in osu!stable, since its source is not public)

1. **Filename override, stable:** does the hitnormal still play? Do additions play? The wiki says additions are dropped, and lazer plays them. *Author (2026-10-29), from experience: stable plays only the file. No hitnormal and no additions.*
2. **sampleSet 0 on a timing point:** does it mean `[General] SampleSet` in stable (as the wiki says), versus `normal` in lazer's parser? *Author (2026-10-29), from experience: yes, it falls back to the map's default. Still confirm with a test map.*
3. **Slider hitSample index, volume and filename in stable:** honoured or ignored (lazer ignores them)? Does stable read anything beyond `n:a` in an edgeSet entry? *Author (2026-10-29): the head and end ignore the slider's own index and volume (the timing point wins, as in lazer). Unsure about the body.*
4. **Slider ticks and sliderslide in stable:** are bank, index and volume evaluated at each tick's own time, or at slider start (lazer: start + 6 ms)? *Author (2026-10-29), unsure: ticks follow the slider body's sampleset. Timing is still open.*
5. **Stable fallback for a missing custom file:** is it the user skin only, or also a bare `hitclap.wav` in the beatmap folder (the lazer quirk)? And for a missing filename-override file?
6. **Set-prefixed or indexed spinner samples** (`soft-spinnerspin2.wav`) in stable.
7. **hitSample volume above 100:** clamped or amplified?
8. **Filename case-sensitivity** of sample lookups (lazer on case-sensitive file systems; the stable editor).
9. **Exact version history of optional fields in v3–v14** (when hitSample, edgeSounds and edgeSets first appeared).
10. **Line endings and encoding stable actually writes** (CRLF? UTF-8 with or without BOM?). Verify by inspecting stable-saved files.
11. **Stable's exact leniency.** Lazer cites stable `HitObject.cs#L319` for +5 ms, but stable's slider-node and body offsets (+5 versus +6 ms) and its spinner end-time lookup cannot be checked directly.
