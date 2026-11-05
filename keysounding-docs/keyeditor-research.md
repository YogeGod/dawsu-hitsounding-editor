# Cubase 15 Key Editor — what was traced

Reference for a later agent. This is Cubase 15.0.30.287 behavior, recovered from the local binary and from one live Key Editor (`Key Editor: Play 01`). Codex builds the dawsu! keysound editor. Do not implement it from this file, and do not invent product details that are not written here.

Separate three kinds of statement below: **verified** (a live hit or a disassembly that was checked against a live hit), **seen** (screen or key log, function not identified), and **unknown**.

## Binary

- File: `C:\Program Files\Steinberg\Cubase 15\Cubase15.exe`
- Size: 135,501,848 bytes. SHA-256 `8da1ef82ab0d005ca87568308212629eb55fa4a0ae01ef0aefc720eccf373763`
- PE x86_64. Preferred image base `0x140000000`, and that was the live base, so file virtual addresses match runtime.
- Module size seen by Frida: 136,626,176.
- The traced process was PID 32292. That pid is not stable across launches.
- No PDB. Ghidra 12.1.4 was used for the static pass. Do not start a second Ghidra on this machine (16 GB heap, 32 GB RAM). REA Ghidra sessions are deleted on close, so a new process pays the full auto-analysis again.

## Product decisions already made

The user confirmed these on 2026-11-05. Do not extend them:

- Bundled piano: yes. Which piano is not decided.
- Multiple instrument tracks: yes. How many is not decided.
- VST3 hosting: yes. Sandbox details are not decided.

Keysound workflow the editor has to meet, already agreed:

1. Use the reference beatmap's red lines.
2. Draw, import, or record MIDI in a separate Keysound project.
3. Sync selected notes to existing gameplay objects. Preview the shift. Default limit 20 ms.
4. Highlight and skip unsynced notes. Do not create extra circles or gameplay objects.
5. Render each synced note to WAV, keeping dynamics, controllers, and the release tail. Reuse identical audio.
6. Generate `[Keysounds]` with only the user's keysounds layered over the existing song. Leave the source difficulty untouched.

## Verified: one semitone

Up and Down are two functions on the note-editor object. Live vtable of that object was `0x1462d53f0` (object `0x17dce4c20` in the session where arrows worked).

| Direction | Function | The add | Vtable slot |
|---|---|---|---|
| Up | `0x14455bc40` | `lea edx, [rax+1]` at `0x14455bc62` | `+0x130` |
| Down | `0x14455bc00` | `lea edx, [rax-1]` at `0x14455bc22` | `+0x128` |

Both read the current dword with vtable `+0xe0` = `0x14455bb30` (`mov eax, [rcx+0x40]; ret`). They then tail-call `[[rcx+0x10]+0x30]`, which was `0x1445595f0`. That calls vtable `+0xd8` = `0x1438301c0`, a thunk to `0x14455bb40`.

`0x14455bb40` calls vtable `+0x108` = `0x1445590d0`. On the in-range path `0x1445591fa` is `mov eax, esi; ret` (esi is the incoming pitch). The store is `0x14455bb57`: `mov dword ptr [rbx+0x40], eax` (bytes `89 43 40`, 3 bytes, not hookable). The next instruction `0x14455bb5a` (`mov edx, 1`) runs only when the stored dword changes. After the store, `0x14455bb40` tail-jumps vtable `+0x50`. `0x1445595f0` then tail-jumps vtable `+0x160` = `0x144559620`, which overwrites rdx and does not itself store the pitch.

The pitch value is the dword at the editor object `+0x40`. MIDI note numbers look like `0x3C` (C4) or `0x25` (C#2). Values `0x1`–`0x13` on other objects are not note pitches.

Evidence: Up/Down entry `ev_fd0f95b51adaf9f122ca1e1edefa91d43d5df194408ebc9a5a840fd2c65fe0ca`. Command ids `ev_afc6f687d95524638958ed5fd257acc6b1df1b12098d848a2afb671ae8b8954e`. Method pointer `ev_4682eec1cd862ae70175077d70ef67883e8b5e4bce1108726fc11c13a22e5772`.

### Same vtable, other slots (disassembled, not all seen live)

- `+0x100` `0x14455bbd0`
- `+0x108` `0x1445590d0` — clamp or return the incoming pitch
- `+0x110` `0x14455be60` — uses dwords at `+0x44` and `+0x48` (`mov eax,[rcx+0x48]; sub eax,[rcx+0x44]`, then a float scale). A length or time helper. Not proven live.
- `+0x118` `0x144559360` — bit `0xB` of `[rcx+0x20]`
- `+0x120` `0x144559390` — bit `0xC` of `[rcx+0x20]`
- `+0x138` `0x144559480` — generic add. Reads pitch via `+0xe0`, then `lea edx,[rdi+rax]` where edi is the incoming delta. Not seen during the user's edits.
- `+0x140` `0x1445594f0`
- `+0x148` `0x144559540`
- `+0x150` `0x1445595d0`
- `+0x158` `0x1445595e0`
- `+0x160` `0x144559620`
- `+0x168` `0x14382d250`
- `+0x170` `0x14382d290`
- `+0x178` `0x144559950`

The subobject vtable at `this+0x10` was `0x1462d5620`. Getter `0x14455bb30`. Setter path `0x1445595f0`.

## Verified: command id versus key id

Packed key id from `0x14481dee0` is `(flag<<8 + command)<<16 + character`.

At the filter, the command byte is: Left 11, Up 12, Right 13, Down 14. Extended Up packs to `0x000C0000`, extended Down to `0x000E0000`.

The executor `0x144857420` receives a different id. Live Up was 9, live Down was 10 (`0xA`). Its jump table is at `0x144857578`, index = id − 3, valid while the index is ≤ `0xB`:

- id 9 → `0x144857530` → `call [rax+0x130]` once (Up)
- id 10 → `0x1448574c0` → `call [rax+0x128]` once (Down)

Raw ids 11, 12, 13, 14 inside that function are different cases and were not the live Up/Down. The hash lookup translates command byte 12 into action id 9 only when `[this+0xb0]` is non-null.

`0x144857420` is vtable `+0x40` of the embedded object at `[container+0x68]`. Live container `0x17d68ec80`, child `[exec+0x10]` = `0x17dce4c20`.

## Verified: when the pitch function does not run

In the later Key Editor session, `[command object+0xb0]` was null. `0x144746fa0` (reached from vtable `+0x518`) then returns at `0x1447471b6`. The test is `test rdx, rdx; je 0x1447471b6` at `0x144746ff3`. Earlier, `[0x3733b60+0xb0]` was `0x17d68ec80` and the executor did call `0x144857420`.

Fallback `0x1447a7b60` walks a global binding list and did not match Left, Right, Up, or Down, so it returned 0 at `0x1447a7be8`. About 100 items, full walk, compare `esi` to `[item+0x14]`. A match would continue at `0x1447a7c0c` (not disassembled).

Evidence of the early-out and the list walk: `ev_93216ee3978984084551be61cfedd47bf8d8b86a76ed45818623ae6d7b61c718`, `ev_03df026707f7fc5d56ee597203672b1384ca6367cdd219dd02a2e56603808752`, `ev_029ca7fa1bfc4215d3413118b4d5e35ec1bcd87dc28a70747efbd457762f4783`.

Call chain that ran for arrows while the pitch function did not:

1. Filter `0x144b19850` sees `WM_KEYDOWN`. Gate byte at `0x14776c0c8` was 0. Classify `0x144b195b0` returns al=1.
2. Deliver at `0x144b199d1`, `call [rax+0x88]`, target `0x144b26d90`, rcx `0x4ac5db50`.
3. `0x144b26d90` loads `[rcx+8]`. Inner object `0x17dde70e0`, vtable `0x1465ba100`, slot `+0x88` = `0x144683460`.
4. `0x14467e240`. Key struct: word at +0 is 0, byte +2 is the command, byte +3 is 0. It calls `0x144694ac0`, which returned rax `0x15f900` (low byte 0, so not accepted). `0x144746fa0` early-outs inside that.
5. Tail at `0x14467e45f`, `jmp [rax+0x190]`, goes to `0x142919450`.
6. If the command byte is not `0x28`, `jmp 0x144689070`, then `jmp 0x1447a7b60`.
7. `0x1447a7b60` reads the command byte at `[rdx+2]`, rejects `0x36`–`0x38`, packs an id with `0x1447ea1e0`, and walks `[this+0xf8]`. List methods: `+0xd0` = `0x141fac980` (count), `+0x108` = `0x141faa3a0` (item). `this` for those calls was `0x1477c18c8`.

`0x14467e240` success return is `mov al, 1` at `0x14467e473`. It did not run on the latest Up. When the pitch path worked, the accepting call was suspected to be `call rbx` at `0x14467e395`. That site could not be hooked. rbx was `[[global 0x1477c0740]+0x518]` = `0x144744f70`, a thunk `jmp 0x144746fa0`. `*0x1477c0740` = `0x3733b60`, vtable `0x1465c9b08`.

## Do not treat these as the semitone function

`0x1446e37f0` names Navigate / Left / Right / Up / Down and calls `[this+0x70]` vtable `+0x40` with edx 7, 8, 9, 10. That function did not run for posted Up in the session where `+0xb0` was null.

`0x1446e4ce0` is a row navigator. If `[rbx+0x90]` is non-null, Up does `dec edi` and Down does `inc edi`. If it is null and bit 21 of `[document+0x88]` is set, it scans rows and sets `edi` to row−1 or row+1. It did not run for the posted Up that was tested. It is not the semitone write.

`0x1437b3a00` is a nudge-size double lookup, not the MIDI pitch increment.

`0x141af3be0` is MIDI Pitch Visibility, not transpose.

Vtable classification of `0x1447c7fc0` is not the live pitch method.

## Seen: edits in the live editor

Log: `E:\Claude\REA\cubase\watch.txt`. Recorder is detached. Cubase was left running.

Baseline when recording started: one selected note, Start `1.1.1.0`, End `1.3.1.0`, Length `0.2.0.0`, Pitch `C4`, Velocity `101`, Off Velocity 64, Channel 1, grid `1/2`.

After cut, undo, select-all, and arrow keys, the info line showed Start `3.1.1.0`, End `3.3.1.0`, Length still `0.2.0.0`, Pitch `C#2`, Velocity still `101`. That time-and-pitch move did not enter `0x14455bc40`, `0x14455bc00`, or `0x144559480`. C4 to C#2 is 23 semitones, so it was not one arrow step. Length unchanged means it was not a resize.

Edit History then showed:

- `65` Delete, details `Note - Note`
- `66`, `67`, `68` Insert Note, each its own executed step, all `Track: Play 01 - Part: Play 0`

Drawing a note is one undo step per note. Deleting is a separate step. Grid readout later showed `1/4`. With several `C2` notes selected, the info line switched to Range Start `3.1.1.0`, Range End `3.1.1.0`, Range Length `0.0.0.0`, Selection Mode `As Selected`.

Later, with no Up or Down key in the log and the mouse button up, the semitone functions ran on one object (`0x72495ba0` in the log): Down 7 times, Up 8 times, Down 13 times. A second object (`0x7248eb20`) then went Down 4 times. `0x144559480` did not run. Those calls are one step each, on one note. The gesture that reached them was not an arrow key. A wheel or a vertical drag is consistent with the log; the button events do not pin which one.

Arrow keys, Ctrl+X, Ctrl+Z, Ctrl+A, Insert, Space, and the mouse clicks in the rest of the log did not enter the semitone functions except for that one burst.

## Undo strings, not on the live path

Strings and their only known code xrefs:

- `Set Note Pitch` at `0x145ee1e78`, code xref `0x14154ff12`. Evidence `ev_76245a93d359de016f6447d4d6b309d4a6a11a1567f7bf8d24801757973bb0b2`.
- `Set Note Velocity` at `0x145ee1e98`
- `Set Off Velocity` at `0x145ee1ec0`
- `Set Release Length` at `0x145ee1f98`

The four registrars `0x14154feb0`, `0x14154ffa0`, `0x141550090`, `0x141550270` were hooked for the whole recording, including the semitone burst and the Insert Note / Delete steps. They never ran. Evidence they were installed: `ev_194eafb7b802b1e1715914c4395b88bafa5fd172591fa002a643438a27e17d0b`. They were also absent from an earlier transpose (`ev_81aed6d729e307aec2abeab43927271a675fd14c72c1576e537dfeab81a72ab9`).

`Insert Note` and `Delete` are the history names that were actually visible. Where those steps are registered is unknown.

## Key Editor names from strings

Evidence `ev_9aaa11257137085b09d3bb97cd9fd00ea431c1c58c59f433c5bd93049ac9ded2`:

- `KeyEditorCLSelectView` `0x145ef7a40`
- `keyeditor-window` `0x145f18a08`
- `keyeditor-lowerzone` `0x145f18a20`
- `KeyEditorLowerZone` `0x145f18bf8`
- `KeyEditorWindow` `0x145f18c10`
- `FOLDED_KEYEDITOR` `0x1460e8890`
- `foldableKeyeditor` `0x146103c38`

String compare convention: `0x144555e20(command, cstring)` returns al=1 on match. `0x1439c7ae0` / `0x1439c7850` is strcmp; eax==0 means equal. Action category name object is at action+`0x48` (`0x1446aaa60`). Sub-action name object is at action+`0x60` (`0x1446aaa90`).

## Still unknown

- Where Left and Right write the start time. Candidates not proven: the dwords at `+0x44` and `+0x48` used by `0x14455be60`.
- Where velocity and note length are stored. The info line can show them, and the undo strings name them, but no live write was found.
- Whether one drag is one undo step. Insert Note was one step per note. The semitone burst did not hit the pitch registrar, so grouping is unproven.
- Snap. The grid control changed from `1/2` to `1/4` on screen. The code that quantizes a drag was not found.
- Whether one Up or Down moves every selected note. The semitone burst hit one object per burst.
- Why `[command object+0xb0]` is null in some Key Editor states, so arrows never reach `0x14455bc40`.
- Mouse draw, select, drag, and resize as functions. The screen showed the results. String names alone are not proof of those operations.
- Which code records `Insert Note` and `Delete`.

## Hooks that are unsafe

- Do not hook strcmp `0x1439c7850` or `0x1439c64d4`.
- Do not hook `0x14455bb40` or `0x14455bb5a` unconditionally. The store is shared with unrelated widgets. It flooded the log with values `0x1`–`0x13` on objects such as `0x157bed80` and stalled the UI. A later hook needs a caller filter or a MIDI-range filter.
- Do not use Frida Stalker.
- Interceptor cannot hook 1–4 byte instructions. The pitch store `89 43 40` is one of those. Hook the 5-byte `mov edx, 1` at `0x14455bb5a` only with a filter.
- These call sites returned "unable to intercept": `0x1446e3c07`, `0x14467e285`, `0x14467e2e7`, `0x14467e395`, `0x14467e3d3`, `0x14467e417`, `0x144747057`, `0x14474706d`, `0x144747173`, `0x1447471ab`. A failed hook is not evidence the call did not run. `onLeave` of a hooked call site often did not fire. `onEnter` of the call site did. `onLeave` of a function entry hook did fire.
- `close_frida_session` detaches and does not kill Cubase. `target_state: unknown` is normal. Confirm with the process list.
- Frida `NativeFunction` pointer arguments need the `pointer` type. `CpuContext` registers are not `NativePointer`s; use `ptr(v)`. Do not use `v >>> 0` for a full pointer.

## Messages

`WM_KEYDOWN` is `0x100`, `WM_KEYUP` is `0x101`, `WM_LBUTTONDOWN` is `0x201`, `WM_LBUTTONUP` is `0x202`. MSG x64 layout: hwnd 0, message 8, wParam 16, lParam 24.

Virtual keys seen in the log: 16 Shift, 17 Ctrl, 18 Alt, 32 Space, 37 Left, 38 Up, 39 Right, 40 Down, 45 Insert, 49 `1`, 50 `2`, 65 A, 88 X, 90 Z, 91 the Windows key. Auto-repeat produces many duplicate `down` lines. Ctrl held during mouse clicks was common and is not, by itself, a select-all.

Posted Up keydown lParam was `0x01480001`, keyup `0xC1480001`. The extended bit was not set, and classification still produced command ids 12 and 14. PostMessage returned 1. Pair a posted Up with a posted Down only when this agent is driving. The user drove the recorded session; do not inject keys into that.

## Tooling notes

- REA 6.8.0 (`rea-agents`). Cursor MCP server name `rea`. Catalog digest SHA-256 `fe938a7c6eba3763b22ae2022de76dc4a7812d35d86f1d4efd36b9962b04da0b`.
- Ghidra `E:\Codex-tools\ghidra_12.1.4_PUBLIC`. JDK `E:\Codex-tools\jdk-21`. Env: `GHIDRA_INSTALL_DIR`, `JAVA_HOME`, `REA_GHIDRA_STARTUP_TIMEOUT_MS=21600000`, `GHIDRA_HEADLESS_MAXMEM=16G`.
- Persistent MCP client: `E:\Claude\REA\cubase\client-entry.mjs`.
- The Write tool emits UTF-16 LE, which Node and Python reject (`source code cannot contain null bytes`). Author scripts with PowerShell `Set-Content -Encoding utf8NoBOM`. If a file is locked, write a new name.
- Static analysis does not prove runtime or audio. Do not clone Cubase or copy its binaries or assets.
- `E:\Claude\AGENTS.md` is generated from `E:\Claude\CLAUDE.md`. Edit the source, not that file. Drum-editor `open-questions.md` is a different feature; do not write these piano or VST3 decisions into it.
