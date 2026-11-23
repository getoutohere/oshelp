# Obenseuer visual style for OSHelp

OSHelp opens in an Obenseuer-inspired dark theme by default, independent of the Windows preference. It uses charcoal/olive surfaces, cream text, ochre actions, and blue navigation. Editors, menus, scrolling surfaces, and application-owned dialogs are explicitly themed on Windows 10. Window chrome and OS file pickers follow Windows.

[OSHelp dark theme concept](assets/theme-preview.svg). The concept profile is illustrative. Its material bill and currency totals come from the extracted Makeshift Bathroom and Makeshift Walls upgrades. Machine-specific application captures are excluded from source releases.

## References inspected

The visual references are Obenseuer's inventory, tenement, and task interfaces:

| Interface | What it establishes |
|---|---|
| Inventory and printing press | Charcoal frame, nearly black inset panels, ochre tabs/actions, cream typewriter labels, red destructive action |
| Tenement | Muted blue floor-navigation buttons, off-white architectural plan, olive requirement icons, red unavailable states |
| Tasks | Black reading surface, raised charcoal list entries, ochre scrollbar, spaced typewriter headings and labels |

Original screenshots and local sampling work are not included in the source release.

Representative 7-by-7 pixel neighborhood modes from the JPEGs are `#2A2A2A` for the inventory frame, `#101010` for inset panels, `#845311` for an ochre action, and `#124A6D` for blue tenement navigation. These are screenshot samples affected by texture, gradients, and JPEG compression, not claims about exact Unity color constants.

The installed assets also contain font assets named `Erika Ormig SDF`, `Chalkiez-Regular SDF`, `NotoSans-Bold SDF`, `Roboto-Regular SDF`, and others. The screenshots clearly establish the typewriter visual treatment. A font asset's presence alone does not establish that every label uses it. The proposed Windows font choices below reproduce the general treatment without adding a font dependency.

The game's off-white architectural plan is a specific document surface. OSHelp's normal tenant lists and material tables use dark surfaces. Architectural drawings can be considered separately if floor-plan views are added later.

## Palette

The implementation source of truth is [design/obenseuer-dark.json](../design/obenseuer-dark.json). The following are deliberately selected application tokens, adjusted from the visual references for legibility and consistent control states.

| Token | Color | Use |
|---|---|---|
| Window | `#181917` | Main background and gutters |
| Panel | `#292A27` | Grouped content areas |
| Recess | `#101210` | Inputs, table rows, detailed reading areas |
| Raised | `#34362F` | Neutral controls and inactive tabs |
| Border | `#55594B` | Quiet separators and panel frames |
| Input border | `#858A76` | Editable control boundaries |
| Text | `#E2DFD2` | Main text, avoiding pure white |
| Muted text | `#B4B5A5` | Supporting text and metadata |
| Accent | `#C09A50` | Selected borders, section labels and important totals |
| Button | `#684812` | Main action fill |
| Button hover | `#805B1F` | Main action hover |
| Selection | `#3B3425` | Selected row fill, paired with an ochre edge |
| Navigation | `#214F65` | Section bars and secondary navigation |
| Navigation hover | `#2C6179` | Secondary navigation hover |
| Success | `#A9BC7A` | Requirements met, accompanied by text or a check |
| Warning | `#D4B474` | Missing work and incomplete states |
| Danger | `#DB9587` | Errors and destructive actions |
| Focus | `#E6C271` | Visible keyboard focus outline |

Keep ochre to selected controls, actions, section labels and important totals; large panels remain dark. Use blue for navigation and context, not every clickable element. Status color is always accompanied by text or an icon, so the user can distinguish states without relying on hue.

## Typography and geometry

- Body: **Segoe UI, 11 pt**, for names, explanations and dense lists.
- Headings: **Courier New Bold, 14 pt**, for the game's typewriter character.
- Short labels and buttons: **Courier New Bold, 10.5 pt**; uppercase only for short sections/actions.
- Quantities: **Consolas, 11 pt**, with numeric columns right-aligned in the actual application.
- Use installed Windows fonts and proper Unicode names. Preserve ordinary case for tenant names and material names.
- Use a four-pixel spacing unit, 20-pixel outer padding, 16-pixel panel padding, 36-pixel controls and approximately 40-pixel grid rows at 96 DPI. Scale from logical units.
- Use square panel/control corners, one-pixel frames and a two-pixel focus indicator. Keep standard window resizing, caption buttons and keyboard behavior.

The visual reference uses worn surfaces and small bevels. The first implementation uses restrained solid fills and frames; busy texture behind text and animated blur would add cost without helping a planning task. Any later texture should be subtle, static, and confined to decorative strips.

## Control styling contract

Theme colors must be applied before the first application window paints. Set the form background before constructing child content; initialize hidden/loading states with the same dark surface. An import failure or empty list must use the themed empty/error state instead of exposing a default control background.

| Control | Required behavior |
|---|---|
| Main form and panels | Explicit background/foreground; dark startup and resize paints; double-buffer frequently painted custom surfaces |
| Navigation | Dark/ochre selected state; dark inactive state; visible focus and hover; use a small custom tab strip or owner-drawn tabs if native tabs expose light chrome |
| `DataGridView` | Set background, default/alternating cells, selection, row/column headers, grid lines and editing-control colors; disable themed header painting; no white new-row/editor surfaces |
| `TreeView` and lists | Explicit colors and selection; preserve standard keyboard behavior; verify focus, inactive selection and scrollbars on Windows 10 |
| Text and number editors | Recess fill, cream text, input border, readable caret/selection, themed validation and disabled states |
| Combo boxes | Themed closed and expanded states; owner-draw only the affected parts when the Windows 10 native popup cannot follow the palette |
| Buttons | Explicit flat fill/border/hover/pressed colors and foreground; set `UseVisualStyleBackColor = false` for palette-owned buttons |
| Menus and tooltips | Palette-aware `ToolStripRenderer`/color table and tooltip rendering, including separators and submenu arrows |
| Status and progress | Dark track with ochre or olive progress; text for exact counts, saves and errors |
| Application dialogs | Themed `Form`-based confirmation/error surfaces with proper dialog results, default/cancel buttons and accessibility; no default white message boxes |
| Native file pickers | OS-managed appearance; follow platform capabilities. Keep OSHelp-owned path/profile forms themed. Test these separately from client controls |

Keep custom paint code small and centralized in a `ThemeService`, `ThemePalette`, and a few reusable controls. Do not add a third-party skinning library or replace the whole interface with a painted bitmap. Windows control semantics and accessibility remain intact.

## Windows 10 and Windows 11 behavior

The [.NET 10 WinForms changes](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100) make `Application.SetColorMode` stable. The [API documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setcolormode?view=windowsdesktop-10.0) limits its native dark color mode to Windows 11 and later and notes high-contrast behavior. Therefore `SetColorMode(Dark)` is an enhancement on supported systems; explicit palette ownership establishes OSHelp's Windows 10 client-area appearance.

Set application color mode before creating controls where it is supported, then apply OSHelp's tokens. Request dark native caption/chrome through supported platform capabilities, retain the standard frame, and verify the result on the actual Windows 10 build. Do not claim that a single DWM attribute guarantees identical chrome across Windows versions. Respect an explicitly enabled accessibility high-contrast mode with an accessible alternative palette.

The theme proof must exercise expanded drop-downs, editing cells, context menus, tooltips, scrollbars, modal dialogs, loading/error states, and resize backgrounds. A dark form with a white editor or popup is unfinished theme work. Verify normal operation on Windows 10 and Windows 11 before accepting the platform/theme implementation.

## Readability and verification

Validate ordinary text at a contrast ratio of at least 4.5:1 against its assigned surface. Visible focus and input boundaries should reach 3:1 against adjoining fills. Quiet decorative dividers can remain more subdued. Status colors are for text/icons on dark surfaces; they are not automatically approved as fills behind cream text.

The initial [contrast check results](../design/contrast-checks.json) cover 23 assigned foreground/background combinations. All pass their assigned text or boundary threshold; the tightest ordinary-text pairing is cream text on the ochre button hover fill at 4.58:1. Recheck these combinations whenever a token or its usage changes.

Check 100%, 150% and 200% DPI, keyboard focus, inactive selection, long Unicode names, and muted text at the actual font sizes. Test startup with the desktop set to Light as well as Dark. Capture the first visible frame during the theme smoke test to catch startup flashes.

Regenerate the preview with no external dependencies:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\oshelp\tools\Render-ThemePreview.ps1
```

The renderer reads the same palette file and produces local PNG and [SVG previews](assets/theme-preview.svg). Only the illustrative SVG is included in the source release. The concept establishes color and layout direction; it does not establish native-control or DPI behavior before the application is implemented.
