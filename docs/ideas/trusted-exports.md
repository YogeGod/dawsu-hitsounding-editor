# Trusted exports

## Problem Statement

How might we help experienced hitsounders trust that dawsu's exported maps reproduce their intended sounds in osu!stable?

## Recommended Direction

Make the next release earn trust through three connected improvements: an independently verified stable test pack, a conflict resolver that blocks Save by default, and a repeatable release checklist.

The test pack establishes actual game behaviour rather than relying solely on agreement between the editor's own exporter and importer. The conflict resolver prevents known unsavable edits from being silently omitted. The release checklist checks both creating hitsound difficulties and editing existing maps.

The release claim is "verified on these cases", with the cases and results published. Passing a finite test pack does not establish universal compatibility.

## Key Assumptions to Validate

- [ ] Small test maps expose important export failures. Validate them alongside representative real mapsets.
- [ ] Listening can distinguish expected results. Use clearly different samples and volumes, with expectations established independently in stable.
- [ ] Users can resolve blocked saves without losing work. Exercise the complete locate, correct, and save flow.
- [ ] Verification is practical to repeat for each release. Trial the checklist and record the effort before making it a release requirement.

## MVP Scope

- Block normal Save when unresolved export conflicts exist; leave the original map unchanged and retain unsaved edits.
- Show each conflict's location, cause, and available resolution. Let users navigate directly to the affected edit.
- Include small test maps covering sample selection, shared volumes, slider behaviour, and generated hitsounds.
- Verify open → edit → save and create → generate → copy workflows in osu!stable.
- Record the app version, stable version, expected result, and observed result for each case.
- Check byte-identical no-edit saves and verify that unrelated mapset files remain untouched.
- Require the agreed cases to pass before release; report failures and untested cases explicitly.

## Not Doing (and Why)

- Automatic conflict repair: it may alter musical intent; users should choose the resolution.
- Export audition interface: adds a separate workflow before basic export reliability is established.
- Comprehensive change-report interface: a focused conflict view is sufficient for this release.
- New editing features: keep the release focused on proving the existing workflow.

## Open Questions

- Which exact cases and representative real mapsets form the initial release checklist?
- What should the conflict resolver offer for each conflict type?
- Should an explicit partial-save command be offered later? Only blocking normal Save by default is settled; an override is not approved.
- Who performs and records stable verification, and what is the acceptable verification time per release?

## Decision Status

Confirmed direction: experienced hitsounders first; next-release scope; independent verification in osu!stable; Save blocks by default on unresolved export conflicts.

This document is a proposal for implementation. No implementation changes or completed verification are implied.
