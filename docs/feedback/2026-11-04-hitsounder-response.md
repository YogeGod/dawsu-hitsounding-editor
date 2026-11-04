# Hitsounder questionnaire response

Received from the user during a paused planning discussion. This is requirements evidence, not implementation approval, a verified format claim, or automatic reprioritization of the owner's roadmap.

## Supplied answers

1. “yes for different family groups of hitsounds”
2. “for example if i only have 1 normal-hitnormal.wav but use a soft-hitclap2 for a additive hitsound, i would need to copy the normal-hitnormal.wav file then past and rename it to normal-hitnormal2.wav to maintain the family sample grouping”
3. “same example to question 2”
4. “similar to question 2 but if i had soft-hitfinish.wav (crash cymbal) and a soft-hitfinish2.wav (church bell) in the same timeline it would be able to coexist due to the sample groupings being different , but if allowed to merge it would create a whole new sample under the name soft-hitfinsih3.wav ( crash cymbal + church bell ). Same can go for a soft-hitfinish and a normal-hitfinish but would fall under the parent hitnormal sample”
5. “yes”
6. “yes and no, depending on the usage of the hitsound described on questions 2 & 4”
7. “only show them when an incompatible combination is identified, other than that it will resort to the merging/fusing method if its intended”
8. “similar to how musical notes work, whole notes (4/1 snaping) dotted half notes (3/1 snapping) half notes (2/1 snapping) and quarter notes (1/1 snapping) is a good representation”
9. “pairing, greenlines vs .osunotepad differentiation for volume/sample changes, & revert to last saved (crtl + l) similar to how osu works”

## Interpretation and limits

- Pairing now has a concrete minimum example: preserve distinct source audio and create a numbered copy of hitnormal to match the addition's custom index. Protect existing files and other difficulties; the example does not authorize overwriting an occupied index.
- Optional fusing creates new audio containing multiple input sounds. Same-timestamp versus different-timestamp coexistence is still ambiguous in answer 4. Do not infer that two sample indexes are independently selectable by one hit object. The wording about a parent hitnormal also needs a concrete export example before selecting an output-slot policy.
- File renaming/renumbering is acceptable. Reuse depends on the particular pairing/mixing case; no precise matching or volume-equivalence rule is confirmed by answer 6.
- Warnings should indicate incompatible combinations, accounting for intended pairing/fusing. This broadly aligns with the owner's hidden missing-object warnings, but requires a clear definition of what the selected save path can resolve.
- Longer snapping is described as steps of 1, 2, 3 and 4 quarter-note beats. Grid origin/phase and behavior across timing changes are not specified. It remains deferred under the owner's earlier scope decision.
- The hitsounder's priorities are pairing, control over green-line versus per-object .osu fields for sample/volume changes, and Ctrl+L for revert. These are stakeholder priorities, not yet the owner's accepted new update order.
- Current source inspection confirms Revert already exists in EditorCommands.cs with no default key assigned. The new request is at least a default Ctrl+L binding; exact parity of its reload/discard behavior remains to be compared with the existing command.
- Green lines versus per-object fields is a distinct export-policy decision. Do not equate a lack of new green lines with failure; establish desired behavior separately for circles and slider-related sounds.
- The map-local/personal-library reusable sets, linked placements, local overrides and mixed/original views explored with the owner are additional owner-requested features. The hitsounder's answers do not establish that they are required for his narrower pairing task.

## Planning status

Later clarification: the owner confirmed answer 4 means the exact same timestamp and instructed prioritizing the hitsounder's answers. The owner has not decided green-line versus per-object field policy because rankability matters. See 2026-11-04-priority-revision.md for the new ordering and live rules research. This later clarification supersedes the same-timestamp ambiguity noted above.

The owner last confirmed Q34A: Add skips an identical already-placed sound and preserves its existing volume. Q33 (prefer separate compatible files versus fewer mixed files) was explained with examples but remains unanswered by the owner. The hitsounder's reply supports separate-file pairing as a core use case but does not itself answer Q33 on the owner's behalf.

Planning remains paused pending the owner's direction. No implementation, merge, commit or release is approved. Compare this response with planning-decisions.md before resuming; do not silently replace confirmed owner decisions.
