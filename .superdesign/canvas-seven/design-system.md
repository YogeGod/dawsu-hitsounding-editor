# Seven preset explorations for Hitsound Editor

The user explicitly authorizes radically different visual styles and layouts, while keeping the editor capabilities. This exploration supersedes the original fixed palette and fixed panel positions for the seven named branches. The original specification below describes the baseline only. In each branch, the named Superdesign preset below is the exclusive visual style source. Use that preset typography, colors, component effects, and rhythm consistently across the entire UI. Domain data remains a music hitsound timeline, not a dashboard or marketing site. Do not combine presets.

## Baseline UI
# Hitsound editor visual design

A native Windows Avalonia desktop editor for osu!standard samples, with Cubase drum-editor workflows. Primary activity: edit sample lanes against a waveform and timeline, audition song and hitsounds, adjust shared moment volume, then save the existing map or generate a hitsound difficulty.

Keep all real controls and commands. Preserve note timing, sample mappings, file behavior, keyboard shortcuts, mute/solo, overview navigation, waveform, object strip, looping, snap, zoom and volume ramps. Diamond note shapes and group color families are fixed. Do not invent a sample browser, side inspector or export feature. The sample-browser button is visibly disabled because it is not implemented. No charts unrelated to the map, marketing copy, logos, gradients or decorative cards.

Target: main editor at 1600x900, also usable at 900x500. Structural hierarchy: compact document identity; dedicated transport and fixed-width LCD counters with Open/Save on the ends; menu/tools/snap/zoom row; view/song-mixer row; whole-song overview; sample list plus synchronized timeline; bottom Volume/What gets saved tabs; status. No overlapping or cropped toolbar controls. Keep dense desktop spacing and readable text.

Palette: background #20242b; transport #282d35; edit #252a32; LCD/input #191e25; controls #333943; borders #414853; active blue #3277bd; main text #e3e8ef; secondary text #9aa7b7; play green #78dba2. Purple Soft, orange Drum, teal Normal, green Own files and blue Skin. Conflicts use red stripes, never red as an ordinary lane hue. Inter UI, Consolas/Cascadia Mono timing. 12-13px UI, 21px counters, 10px captions. Corners 4-6px. The current source is the local working revision; the before screenshot shows the previous overlapping light-chrome layout.

Native custom-drawn lanes stay custom-drawn in implementation. A Superdesign HTML draft is a visual review aid and must not replace the C# app or pretend to edit real maps.


## Safe demo data
Use Example Track [Hitsounds], normal-hitnormal.wav, soft-hitclap.wav, drum-hitfinish.wav, custom-shaker.wav and other generic sample labels. No real user song titles, sample collections, local file paths, or audio are supplied. Text brand only: HITSOUND EDITOR. No logo asset is required.

## Behavioral constraints for all designs
Preserve Open and Save .osu, transport controls and loop, musical/time/BPM/signature counters, editing tools Select/Draw/Erase/Mute/Ramp, snap and zoom, lane detail/volume toggles, song-only volume/mute/offset, overview waveform, scrollable colored sample lanes with M/S toggles and diamond hit markers, timeline ruler and playhead/locators, bottom volume automation with ramp and selection controls, status. Sample browser stays visibly disabled. A right panel may reorganize existing volume controls; do not invent features. Keep the central timeline visually dominant with a visible waveform, more than 12 lanes and many aligned diamond marks. A 1600x900 desktop app view should fill its viewport, not be shown inside a marketing device frame.

## Actual Superdesign preset definitions
### Bauhaus
`json
{
  "name": "Bauhaus",
  "id": "69e1542e-e5e4-4289-9faa-a65fa3f6cea1",
  "light": {
    "card": "#FFFFFF",
    "ring": "#121212",
    "input": "#121212",
    "muted": "#E0E0E0",
    "accent": "#F0C020",
    "border": "#121212",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#D02020",
    "sidebar": "#ffffff",
    "secondary": "#1040C0",
    "background": "#F0F0F0",
    "foreground": "#121212",
    "destructive": "#D02020",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#121212",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#121212",
    "accentForeground": "#121212",
    "popoverForeground": "#09090b",
    "primaryForeground": "#FFFFFF",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#FFFFFF",
    "destructiveForeground": "#FFFFFF",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#09090b",
    "ring": "#d4d4d8",
    "input": "#27272a",
    "muted": "#27272a",
    "accent": "#27272a",
    "border": "#27272a",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#fafafa",
    "sidebar": "#18181b",
    "secondary": "#27272a",
    "background": "#09090b",
    "foreground": "#fafafa",
    "destructive": "#7f1d1d",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#fafafa",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#a1a1aa",
    "accentForeground": "#fafafa",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#18181b",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#fafafa",
    "destructiveForeground": "#fafafa",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "4px 4px 0px 0px #121212",
    "borderRadius": "0px"
  },
  "typography": {
    "fontSize": "16",
    "fontFamily": "\"Outfit\", sans-serif",
    "lineHeight": "1.1",
    "letterSpacing": "0",
    "fontFamilyMono": "var(--font-geist-mono), monospace",
    "fontWeightBold": "900",
    "fontFamilySerif": "Georgia, Times New Roman, serif",
    "fontWeightRegular": "500",
    "headingLetterSpacing": "-0.02em"
  }
}
`

### Beam
`json
{
  "name": "Beam",
  "id": "e5fdd65d-8c90-4834-bfdd-a5f4b23b1ca8",
  "light": {
    "card": "#ffffff",
    "ring": "#0a0a0a",
    "input": "#e5e5e5",
    "muted": "#f5f5f5",
    "accent": "#ffaa40",
    "border": "#e5e5e5",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#171717",
    "sidebar": "#ffffff",
    "secondary": "#f5f5f5",
    "background": "#ffffff",
    "foreground": "#0a0a0a",
    "destructive": "#ef4444",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#0a0a0a",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#737373",
    "accentForeground": "#171717",
    "popoverForeground": "#09090b",
    "primaryForeground": "#ffffff",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#171717",
    "destructiveForeground": "#ffffff",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#171717",
    "ring": "#ffaa40",
    "input": "#262626",
    "muted": "#262626",
    "accent": "#ffaa40",
    "border": "#262626",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#fafafa",
    "sidebar": "#18181b",
    "secondary": "#262626",
    "background": "#0a0a0a",
    "foreground": "#fafafa",
    "destructive": "#f43f5e",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#fafafa",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#a3a3a3",
    "accentForeground": "#171717",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#171717",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#fafafa",
    "destructiveForeground": "#fafafa",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "0 1px 3px 0 rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1)",
    "borderRadius": "0.625rem"
  },
  "typography": {
    "fontSize": "16",
    "fontFamily": "Inter, sans-serif",
    "lineHeight": "1.5",
    "letterSpacing": "0",
    "fontFamilyMono": "var(--font-geist-mono), monospace",
    "fontWeightBold": "700",
    "fontFamilySerif": "Georgia, Times New Roman, serif",
    "fontWeightRegular": "400",
    "headingLetterSpacing": "-0.02em"
  }
}
`

### Kinetic
`json
{
  "name": "Kinetic",
  "id": "e9b91892-5934-400f-9356-e5fe80054172",
  "light": {
    "card": "#09090B",
    "ring": "#DFE104",
    "input": "#3F3F46",
    "muted": "#27272A",
    "accent": "#DFE104",
    "border": "#3F3F46",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#DFE104",
    "sidebar": "#ffffff",
    "secondary": "#27272A",
    "background": "#09090B",
    "foreground": "#FAFAFA",
    "destructive": "#EF4444",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#FAFAFA",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#A1A1AA",
    "accentForeground": "#000000",
    "popoverForeground": "#09090b",
    "primaryForeground": "#000000",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#FAFAFA",
    "destructiveForeground": "#FAFAFA",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#09090B",
    "ring": "#DFE104",
    "input": "#3F3F46",
    "muted": "#27272A",
    "accent": "#DFE104",
    "border": "#3F3F46",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#DFE104",
    "sidebar": "#18181b",
    "secondary": "#27272A",
    "background": "#09090B",
    "foreground": "#FAFAFA",
    "destructive": "#EF4444",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#FAFAFA",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#A1A1AA",
    "accentForeground": "#000000",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#000000",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#FAFAFA",
    "destructiveForeground": "#FAFAFA",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "none",
    "borderRadius": "0px"
  },
  "typography": {
    "fontSize": "16",
    "fontFamily": "\"Space Grotesk\", \"Inter\", sans-serif",
    "lineHeight": "1.2",
    "letterSpacing": "-0.02em",
    "fontFamilyMono": "var(--font-geist-mono), monospace",
    "fontWeightBold": "700",
    "fontFamilySerif": "Georgia, Times New Roman, serif",
    "fontWeightRegular": "400",
    "headingLetterSpacing": "-0.02em"
  }
}
`

### Luxury
`json
{
  "name": "Luxury",
  "id": "150ccb6a-09f1-428b-a43c-46e34dd0e51b",
  "light": {
    "card": "#F9F8F6",
    "ring": "#D4AF37",
    "input": "#1A1A1A",
    "muted": "#EBE5DE",
    "accent": "#D4AF37",
    "border": "#1A1A1A",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#1A1A1A",
    "sidebar": "#ffffff",
    "secondary": "#EBE5DE",
    "background": "#F9F8F6",
    "foreground": "#1A1A1A",
    "destructive": "#ef4444",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#1A1A1A",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#6C6863",
    "accentForeground": "#FFFFFF",
    "popoverForeground": "#09090b",
    "primaryForeground": "#FFFFFF",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#1A1A1A",
    "destructiveForeground": "#FFFFFF",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#09090b",
    "ring": "#d4d4d8",
    "input": "#27272a",
    "muted": "#27272a",
    "accent": "#27272a",
    "border": "#27272a",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#fafafa",
    "sidebar": "#18181b",
    "secondary": "#27272a",
    "background": "#09090b",
    "foreground": "#fafafa",
    "destructive": "#7f1d1d",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#fafafa",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#a1a1aa",
    "accentForeground": "#fafafa",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#18181b",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#fafafa",
    "destructiveForeground": "#fafafa",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "0 4px 16px rgba(0,0,0,0.08)",
    "borderRadius": "0px"
  },
  "typography": {
    "fontSize": "16px",
    "fontFamily": "\"Inter\", sans-serif",
    "lineHeight": "1.625",
    "letterSpacing": "0em",
    "fontFamilyMono": "\"Fira Code\", monospace",
    "fontWeightBold": "700",
    "fontFamilySerif": "\"Playfair Display\", serif",
    "fontWeightRegular": "400",
    "headingLetterSpacing": "-0.02em"
  }
}
`

### Neumorphism
`json
{
  "name": "Neumorphism",
  "id": "5f89e132-cd45-4bf7-829b-b2e39ecffa22",
  "light": {
    "card": "#ffffff",
    "ring": "#18181b",
    "input": "#e4e4e7",
    "muted": "#D1D9E6",
    "accent": "#FF8800",
    "border": "#e4e4e7",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#FF8800",
    "sidebar": "#ffffff",
    "secondary": "#E2E6EA",
    "background": "#EBECF0",
    "foreground": "#4A5568",
    "destructive": "#ef4444",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#09090b",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#718096",
    "accentForeground": "#FFFFFF",
    "popoverForeground": "#09090b",
    "primaryForeground": "#FFFFFF",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#4A5568",
    "destructiveForeground": "#fafafa",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#09090b",
    "ring": "#d4d4d8",
    "input": "#27272a",
    "muted": "#27272a",
    "accent": "#27272a",
    "border": "#27272a",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#FF8800",
    "sidebar": "#18181b",
    "secondary": "#E2E6EA",
    "background": "#EBECF0",
    "foreground": "#4A5568",
    "destructive": "#7f1d1d",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#fafafa",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#a1a1aa",
    "accentForeground": "#fafafa",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#FFFFFF",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#4A5568",
    "destructiveForeground": "#fafafa",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "0 1px 3px 0 rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1)",
    "borderRadius": "0.625rem"
  },
  "typography": {
    "fontSize": "16",
    "fontFamily": "var(--font-geist-sans), system-ui, sans-serif",
    "lineHeight": "1.5",
    "letterSpacing": "0",
    "fontFamilyMono": "var(--font-geist-mono), monospace",
    "fontWeightBold": "700",
    "fontFamilySerif": "Georgia, Times New Roman, serif",
    "fontWeightRegular": "400",
    "headingLetterSpacing": "-0.02em"
  }
}
`

### Newsprint
`json
{
  "name": "Newsprint",
  "id": "5ca7c2aa-7d65-46b9-98d6-75d821b786ac",
  "light": {
    "card": "#F9F9F7",
    "ring": "#111111",
    "input": "#111111",
    "muted": "#E5E5E0",
    "accent": "#CC0000",
    "border": "#111111",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#111111",
    "sidebar": "#ffffff",
    "secondary": "#E5E5E0",
    "background": "#F9F9F7",
    "foreground": "#111111",
    "destructive": "#CC0000",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#111111",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#737373",
    "accentForeground": "#F9F9F7",
    "popoverForeground": "#09090b",
    "primaryForeground": "#F9F9F7",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#111111",
    "destructiveForeground": "#F9F9F7",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#111111",
    "ring": "#F9F9F7",
    "input": "#F9F9F7",
    "muted": "#262626",
    "accent": "#CC0000",
    "border": "#F9F9F7",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#F9F9F7",
    "sidebar": "#18181b",
    "secondary": "#262626",
    "background": "#111111",
    "foreground": "#F9F9F7",
    "destructive": "#CC0000",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#F9F9F7",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#A3A3A3",
    "accentForeground": "#F9F9F7",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#111111",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#F9F9F7",
    "destructiveForeground": "#F9F9F7",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "4px 4px 0px 0px #111111",
    "borderRadius": "0px"
  },
  "typography": {
    "fontSize": "16",
    "fontFamily": "Lora",
    "lineHeight": "1.5",
    "letterSpacing": "0",
    "fontFamilyMono": "'JetBrains Mono', monospace",
    "fontWeightBold": "700",
    "fontFamilySerif": "Playfair Display",
    "fontWeightRegular": "400",
    "headingLetterSpacing": "-0.02em"
  }
}
`

### Win98
`json
{
  "name": "Win98",
  "id": "b3a5050d-dd44-4738-b9d2-afb27980c69a",
  "light": {
    "card": "#C0C0C0",
    "ring": "#000000",
    "input": "#FFFFFF",
    "muted": "#808080",
    "accent": "#0000FF",
    "border": "#000000",
    "chart1": "#e76e50",
    "chart2": "#2a9d90",
    "chart3": "#274754",
    "chart4": "#e8c468",
    "chart5": "#f4a462",
    "popover": "#ffffff",
    "primary": "#000080",
    "sidebar": "#ffffff",
    "secondary": "#FF0000",
    "background": "#C0C0C0",
    "foreground": "#000000",
    "destructive": "#FF0000",
    "sidebarRing": "#a1a1aa",
    "sidebarAccent": "#f4f4f5",
    "sidebarBorder": "#e4e4e7",
    "cardForeground": "#000000",
    "sidebarPrimary": "#18181b",
    "mutedForeground": "#FFFFFF",
    "accentForeground": "#FFFFFF",
    "popoverForeground": "#09090b",
    "primaryForeground": "#FFFFFF",
    "sidebarForeground": "#3f3f46",
    "secondaryForeground": "#FFFFFF",
    "destructiveForeground": "#FFFFFF",
    "sidebarAccentForeground": "#18181b",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "dark": {
    "card": "#404040",
    "ring": "#000000",
    "input": "#808080",
    "muted": "#404040",
    "accent": "#0000FF",
    "border": "#808080",
    "chart1": "#2662d9",
    "chart2": "#2eb88a",
    "chart3": "#e88c30",
    "chart4": "#af57db",
    "chart5": "#e23670",
    "popover": "#09090b",
    "primary": "#C0C0C0",
    "sidebar": "#18181b",
    "secondary": "#000080",
    "background": "#000000",
    "foreground": "#FFFFFF",
    "destructive": "#FF0000",
    "sidebarRing": "#3f3f46",
    "sidebarAccent": "#27272a",
    "sidebarBorder": "#27272a",
    "cardForeground": "#FFFFFF",
    "sidebarPrimary": "#2563eb",
    "mutedForeground": "#C0C0C0",
    "accentForeground": "#FFFFFF",
    "popoverForeground": "#fafafa",
    "primaryForeground": "#000000",
    "sidebarForeground": "#a1a1aa",
    "secondaryForeground": "#FFFFFF",
    "destructiveForeground": "#FFFFFF",
    "sidebarAccentForeground": "#fafafa",
    "sidebarPrimaryForeground": "#fafafa"
  },
  "effects": {
    "boxShadow": "inset -1px -1px 0 #404040, inset 1px 1px 0 #dfdfdf",
    "borderRadius": "0px"
  },
  "typography": {
    "fontSize": "16px",
    "fontFamily": "\"MS Sans Serif\", \"Segoe UI\", Tahoma, Geneva, Verdana, sans-serif",
    "lineHeight": "1.5",
    "letterSpacing": "0em",
    "fontFamilyMono": "\"Courier New\", Courier, monospace",
    "fontWeightBold": "700",
    "fontFamilySerif": "\"Arial Black\", Impact, Haettenschweiler, sans-serif",
    "fontWeightRegular": "400",
    "headingLetterSpacing": "0em"
  }
}
`

## Required branch compositions
### 01 · Bauhaus
Create the Bauhaus alternative; title it 01 · Bauhaus. Apply ONLY the actual Bauhaus preset section, light palette: off-white #F0F0F0, ink #121212, red #D02020 primary, blue #1040C0 secondary and yellow #F0C020 accents. Outfit, 900-weight headings, square controls, strong black outlines and 4px hard offset shadows. Recompose the whole editor with a narrow left vertical editing-tool rail, a strong horizontal top transport band and the timeline to its right. Use bold but compact typographic hierarchy and geometric control blocks. Keep the waveform/timeline dominant, existing sample-group colors identifiable, volume automation aligned below the timeline. These are functional controls, not decorative poster geometry. Change control shapes, hierarchy, spacing, panel grouping and layout together.

### 02 · Beam
Create the Beam alternative; title it 02 · Beam. Apply ONLY the actual Beam preset section, LIGHT white #ffffff background, near-black #0a0a0a text, #171717 primary, warm #ffaa40 accent, #e5e5e5 borders. Inter, 10px radii, very subtle shadows. Recompose into a calm productivity workspace: slim menu/document row, compact two-tier command header, centered rounded transport pod above a large light timeline, slim left sample column and integrated bottom automation drawer. Place Open and Save together in the upper right; group song volume/mute/offset beside playback. Keep micro-labels and separators crisp, generous outer breathing room but dense enough lane rows for serious editing. No big empty cards or dashboard statistics. Transform all controls and surfaces consistently.

### 03 · Kinetic
Create the Kinetic alternative; title it 03 · Kinetic. Apply ONLY the actual Kinetic preset section: #09090B near-black background, #FAFAFA white, acid #DFE104 primary/accent, #3F3F46 borders; Space Grotesk with Inter fallback; sharp zero-radius controls, no shadows. Make a striking professional music workstation: very compact command header, vertical tool rail at left, maximal-width waveform and note timeline, volume automation directly below it, and a bold full-width BOTTOM transport dock with large time/beat counters and acid-yellow play/loop/active controls. Reposition Open/Save in the slim top header. Dense rows, clear grid lines and restrained acid accents. Keep existing lane color families distinct against the dark grid. This is an editor, not a music landing page.

### 04 · Luxury
Create the Luxury alternative; title it 04 · Luxury. Apply ONLY the actual Luxury preset section: warm #F9F8F6 surfaces, #1A1A1A ink and #D4AF37 gold highlights, square geometry with fine outlines, subtle 0 4px 16px shadows. Inter for UI, Playfair Display for the small document heading, Fira Code counters. Recompose as a refined studio console: elegant compact header, central transport, a large light timeline and left sample rows, plus a narrow RIGHT-SIDE volume/ramp control panel made from existing volume controls. Keep the automation lane beneath the timeline but move numeric/ramp/selection controls to that right panel. Do not invent extra inspector properties. Use precise thin rules, understated active states and an obvious Save action. Avoid luxury marketing imagery or excessive headline size; use the preset across the real editor controls.

### 05 · Neumorphism
Create the Neumorphism alternative; title it 05 · Neumorphism. Apply ONLY the actual Neumorphism preset section: cool #EBECF0 background, #4A5568 text, orange #FF8800 primary/accent, Geist/system sans, rounded 10px controls and the preset soft shadows. Make the surface treatment visibly tactile with softly raised grouped controls and recessed LCD/input/slider wells in that same palette; keep active states and labels highly readable. Recompose into a broad timeline workspace on the right, a compact LEFT utility column holding transport counters and song mixer above the sample list, a horizontal top editing toolbar, and integrated volume automation below. The timeline must still receive most width and show many lanes. Differentiate raised button, pressed tool, input, slider and panel treatments consistently. Avoid a generic white-card dashboard.

### 06 · Newsprint
Create the Newsprint alternative; title it 06 · Newsprint. Apply ONLY the actual Newsprint preset section: paper #F9F9F7, ink #111111, red #CC0000 accent, Lora serif UI, Playfair Display small masthead/document heading and JetBrains Mono for technical counters. Square corners, strong ink rules, crisp 4px black offset shadows only on key controls. Recompose as an editorial instrument: compact masthead/document identity, a ruled menu/tool strip, very clear numbered sample list and large light timeline as the main spread, then transport and automation in a bottom band. Place Open/Save into the upper masthead corners. Keep serif text readable at UI scale, timeline numbers monospace, red reserved for small active indicators and conflict semantics. Retain colored lane-family markers as data rather than turning everything monochrome. No news articles, columns of prose or fake newspaper content.

### 07 · Win98
Create the Win98 alternative; title it 07 · Win98. Apply ONLY the actual Win98 preset section: #C0C0C0 gray chrome, #000080 navy document bar/selection, #000000 text, #0000FF accent, MS Sans Serif/Segoe UI/Tahoma UI and Courier New counters, zero radii and exact raised/sunken beveled borders from its inset shadow definition. Recompose as a fully realized late-1990s desktop sequencer: navy internal document title strip, menu FIRST directly below it, chunky square transport toolbar with inset counter display next, then tool/view/mixer controls, overview, timeline and bottom volume tabs/status. Gray scrollbars, beveled splitters and classic pressed M/S and tool buttons should follow the same language. Use the full viewport without adding a surrounding fake computer or browser frame. Preserve the waveform, colored diamond notes and every meaningful editor control.