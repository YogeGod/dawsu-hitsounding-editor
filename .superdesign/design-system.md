# Hitsound editor visual design

A native Windows Avalonia desktop editor for osu!standard samples, with Cubase drum-editor workflows. Primary activity: edit sample lanes against a waveform and timeline, audition song and hitsounds, adjust shared moment volume, then save the existing map or generate a hitsound difficulty.

Keep all real controls and commands. Preserve note timing, sample mappings, file behavior, keyboard shortcuts, mute/solo, overview navigation, waveform, object strip, looping, snap, zoom and volume ramps. Diamond note shapes and group color families are fixed. Do not invent a sample browser, side inspector or export feature. The sample-browser button is visibly disabled because it is not implemented. No charts unrelated to the map, marketing copy, logos, gradients or decorative cards.

Target: main editor at 1600x900, also usable at 900x500. Structural hierarchy: compact document identity; dedicated transport and fixed-width LCD counters with Open/Save on the ends; menu/tools/snap/zoom row; view/song-mixer row; whole-song overview; sample list plus synchronized timeline; bottom Volume/What gets saved tabs; status. No overlapping or cropped toolbar controls. Keep dense desktop spacing and readable text.

Palette: background #20242b; transport #282d35; edit #252a32; LCD/input #191e25; controls #333943; borders #414853; active blue #3277bd; main text #e3e8ef; secondary text #9aa7b7; play green #78dba2. Purple Soft, orange Drum, teal Normal, green Own files and blue Skin. Conflicts use red stripes, never red as an ordinary lane hue. Inter UI, Consolas/Cascadia Mono timing. 12-13px UI, 21px counters, 10px captions. Corners 4-6px. The current source is the local working revision; the before screenshot shows the previous overlapping light-chrome layout.

Native custom-drawn lanes stay custom-drawn in implementation. A Superdesign HTML draft is a visual review aid and must not replace the C# app or pretend to edit real maps.
