# Prioritize the hitsounder's requirements

## Latest direction

The owner confirmed that the overlapping samples in the hitsounder's answer 4 occur at the exact same timestamp, and instructed prioritizing his answers. The desired green-line versus per-object policy is undecided, contingent on rankability evidence. These directions supersede the earlier plan ordering, not the requirement for final plan approval before implementation.

## Revised order for planning

1. Basic pairing: prepare compatible numbered sample files so distinct sounds play together without manual copy/rename. Preserve original files and protect samples shared by other difficulties. Example: provide normal-hitnormal2.wav containing the original normal-hitnormal.wav audio to accompany soft-hitclap2.
2. Same-timestamp finish fusion: preserve original editable inputs and retain input balances. Two finish samples at one time cannot be independently selected by a single object's finish flag merely because their indexes differ. The owner's latest correction requires a new finish sample even when inputs come from different samplesets. Keep the existing hitnormal separate and pair it with the fused finish. Do not route this combination into hitnormal.
3. Decide green-line versus per-object sample/volume writes using current rules and stable compatibility evidence. Do not guess slider behavior from circle behavior. Keep source maps intact during checks.
4. Add default Ctrl+L for the existing Revert command, preserving existing confirmation and reload behavior unless a specific behavior change is agreed. Check custom-keybinding conflicts and do not silently overwrite user choices.
5. Follow with the agreed warning display and other workflow improvements. False-warning classification belongs with pairing/export correctness because resolvable combinations should not be presented as irrecoverable failures.

The personal set library, linked placements, local overrides, set setup/placement tools and direct mixed-audio editing are future features. Preserve the owner's earlier answers for that follow-up. Longer snap is now semantically clearer (1/1, 2/1, 3/1 and 4/1 correspond to one through four quarter-note beats), but was not explicitly moved into the first implementation scope by this priority instruction.

## Rankability research, checked 2026-11-04

Primary sources retrieved live:

- [General ranking criteria](https://osu.ppy.sh/wiki/en/Ranking_criteria)
- [osu!standard ranking criteria](https://osu.ppy.sh/wiki/en/Ranking_criteria/osu%21)
- [.osu format documentation](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29)

The format explicitly documents per-object hitSample index and volume, including zero values inheriting the timing point's settings. The current general and osu!standard criteria inspected do not state a blanket prohibition on these fields or a blanket requirement to express all hitsound changes using green lines. This supports investigating per-object writes; it does not establish that every use or output map is rankable.

Relevant explicit requirements include audible player feedback, minimum hitsound-file duration, the prescribed silent sample, no unused/empty mapset files, and restrictions on duplicate timing points. Generated output must be evaluated as a complete mapset. Do not delete original or shared source samples automatically to satisfy packaging hygiene.

Required evidence before picking a default: isolated circle and slider cases, reopen/save behavior in the supported client, actual sample/index/volume resolution and impact on other objects sharing timing points. Ranking acceptance and client playback correctness are separate questions. Mapset Verifier is recommended by the criteria but was not run in this planning turn. No new manual gameplay/audio check was performed.

## Remaining planning decisions

- Q35A is now confirmed: two different simultaneous finish sounds automatically fuse into a new finish sample, while the project retains the original editable layers. Review output provides overrides. Other cross-bank destination cases still require a deliberate policy.
- Historical Q37B is superseded: the owner explicitly corrected cross-bank finish fusion to produce a finish sample, not a hitnormal. Pair the original hitnormal separately. This latest correction takes precedence over prior notes.
- Q38A then confirms the distinct-addition case: normal hitnormal kick + soft clap + drum whistle stay separate using identical audio copies in compatible sample slots at a common index. Do not fuse merely because input banks differ.
- Exact scope of the first pairing/export update, with the broader reusable-set library excluded.
- Confirmed command scope Q36B: Save .osu, Generate and Copy hitsounds all use the new pairing/fusing behavior while retaining their distinct purposes.
- Q39C confirms a selectable export style: default green lines, optional per-object settings where supported and verified. A lack of new green lines in the optional mode alone is not a failed export. Preserve untouched settings and validate sliders separately.
- Final concrete implementation plan approval.

No application code, commit, merge, release or remote publication is authorized by this document.
