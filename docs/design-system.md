# Design system

The app's look comes from one set of tokens and one style file. The values follow the Figma file
"dawsu! Keysounding — first screen" (frames *Arrangement review* and *Key Editor*), whose variables and button
components mirror the tokens below.

## Where things live

| Part | File | What it holds |
| --- | --- | --- |
| Tokens | `src/OsuHitsound.App/UiTheme.cs` | Every colour, size and font size, with the Figma values as defaults. The UI inspector (View → UI inspector) edits them live; changes go to `ui-theme.json`. |
| Resources | `src/OsuHitsound.App/ThemeResources.cs` | Puts the tokens into the application's resources (`ButtonBg`, `ChromeBorder`, …) so every window and dialog uses them. |
| Styles | `src/OsuHitsound.App/DesignSystem.axaml` | The control styles and classes. Screens use classes instead of setting colours. |
| Icons | `src/OsuHitsound.App/Icons.axaml`, `AppIcon.cs` | Lucide icons as geometry; `<app:AppIcon Glyph="Scissors" />` draws one in the text colour. |
| Fonts | `Assets/Fonts` | Inter (text, from Avalonia.Fonts.Inter) and Roboto Mono (`MonoFont`, the time displays). |

## Palette

| Token | Value | Used for |
| --- | --- | --- |
| `window.bg` / `window.text` | `#adadad` / `#252525` | The window's own title bar |
| `title.bg` | `#555555` | Menu bar |
| `control.bg` | `#494949` | Toolbar |
| `edit.bg`, `footer.bg` | `#2b2b2b` | Info bar, footer |
| `status.bg`, `panel.bg` | `#383838` | Transport, panels, lanes |
| `panel.raised` | `#515151` | Track headers, cards |
| `chrome.border` | `#171a1c` | Edges between bars |
| `line.divider` | `#5e5e5e` | Dividers inside bars, bar lines |
| `button.text` / `text.secondary` / `text.dim` | `#edf0f1` / `#c1c7cc` / `#bdbdbd` | Text, three levels |
| `text.onAccent` | `#ffffff` | Text and icons on blue, green, gold and red buttons |
| `window.close` | `#c42b1c` | Close button under the pointer |
| `brand` | `#ff66aa` | The app mark |
| `button.bg` / `button.border` | `#4d4d4d` / `#414649` | Buttons, combo boxes |
| `button.active` / `button.activeBorder` | `#287ac3` / `#7358df` | Primary buttons, switched-on toggles |
| `tool.active` / `tool.activeIcon` | `#bfc5c8` / `#252525` | The selected tool |
| `play.bg`, `cycle.bg`, `record.bg` | `#1cac46`, `#b19443`, `#c8393d` | Playing, Cycle on, recording |
| `lcd.bg` / `lcd.text` / `lcd.caption` | `#242a32` / `#ccdbea` / `#afbfce` | Time display |
| `slider.track` / `slider.thumb` | `#5e5e5e` / `#c1c7cc` | Sliders: 3 px track, 8 × 12 handle |

Sizes: controls are 30 px high with 2 px corners (`button.height`, `button.radius`); tools are 30 × 30 and joined
1 px apart; transport buttons are 38 × 32 and joined.

## Classes

| Class | Looks like |
| --- | --- |
| `Button` (none) | Grey button with a dark border |
| `Button.primary` | Blue, semibold: the screen's main action |
| `Button.flat` | Text only; lights up on hover |
| `Button.icon` | Square 30 × 30 icon button |
| `ToggleButton` (none) | Grey; blue when on |
| `ToggleButton.tool` | One tool in a joined row; light with a dark icon when selected |
| `.transport` | Joined transport button; `Button.transport.playing` is green, `ToggleButton.transport` on is gold, `.record` on is red |
| `RadioButton.tab` | Tab with an underline in the accent colour |
| `Border.header`, `.toolbar`, `.info`, `.transportBar`, `.footer` | The bars, top to bottom |
| `TextBlock.fieldLabel`, `.fieldValue`, `.toolLabel`, `.lcd`, `.caption`, `.mono` | Labels, values, the time display |

## Adding to it

- A new colour or size: add a setting to `UiTheme.Parts`, a resource name in `ThemeResources.Apply`, and use it with
  `{DynamicResource Name}`. Keep the Figma variable of the same name in step.
- A new icon: add the Lucide SVG to the converter's input and regenerate `Icons.axaml` (each SVG element becomes one
  figure; a path's leading `m` is made absolute).
- Drawn views (`ArrangeView`, `PianoRollView`) read tokens through `Paint`.
