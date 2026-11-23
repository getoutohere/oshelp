# OSHelp 0.1

OSHelp is a Windows desktop companion for **Obenseuer**. Plan tenement renovations, find tenants, keep a materials shopping list, and check vendor opening hours with entrance locks taken into account.

**Version 0.1 is distributed as source only. Build it locally using your own installed copy of Obenseuer.** Compiled applications, game assemblies, decompiled game source, personal saves, and profiles are not included.

## License

OSHelp uses the [OSHelp Noncommercial License with Loiste Interactive Commercial Permission](LICENSE), version 1.0 (`LicenseRef-OSHelp-NC-Loiste-1.0`). This is a custom source-available license.

- Everyone may use, study, modify, and redistribute OSHelp for noncommercial purposes, without paying a license fee.
- Loiste Interactive has an explicit royalty-free permission to commercialize it, including integration into games, products, and services. Other users need separate permission for commercial use.
- Redistributed copies and derivative works must include the complete license. No separate credit, acknowledgment, advertising, or UI attribution is required. The license-copy requirement also applies to Loiste Interactive.
- The license covers only rights controlled by OSHelp's licensors. Obenseuer and third-party game code, dialogue, artwork, and other assets retain their own ownership and terms.

Contributions submitted for inclusion must be offered under these same terms, including the Loiste Interactive permission. Do not submit material you lack permission to license. Original additions in an independent fork are not automatically licensed by their authors under OSHelp's terms.

## Features

- Browse tenant requirements, recruitment locations, and current renters. Compare actual rent payments when considering a replacement tenant.
- Inspect renovation bills, prerequisites, qualified contractors, prices, and construction times.
- Plan work across multiple apartments and shared building facilities. The shopping cart counts shared prerequisites once and subtracts your recorded stock.
- Import a game save, then **Sync latest save** to update apartments, renters, construction, and selected material stock while retaining your shopping goals.
- Manage separate profiles, export shopping lists, and delete profiles with confirmation and a recovery copy.
- Browse one row per vendor business, with combined hours that account for doors, weekdays, and known access conditions.

Data comes from the installed assets and reviewed game rules. The shop and player apartment are excluded from tenant rentals. The baseline covers 31 resident definitions, 131 upgrades, and 96 vendor businesses or machine groups; counts describe the verified game build.

## Requirements

| Requirement | Details |
| --- | --- |
| Operating system | Windows 10 or later; verified on Windows x64. |
| Game | An installed Windows copy of Obenseuer with its `Obenseuer_Data` directory. OSHelp validates the game assembly against its reviewed baseline and stops if unsupported game code is detected. |
| Build tools | [.NET 8 SDK for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), version 8.0.400 or later in the 8.0 SDK series. Verified with SDK 8.0.407. Visual Studio and Unity are not required. |
| Runtime | .NET Desktop Runtime 8.0 or later. The Windows .NET 8 SDK includes its desktop runtime; OSHelp permits newer installed major versions and has also been verified with Desktop Runtime 10.0.9. |
| Asset reader | Windows PowerShell 5.1 (`powershell.exe`), included with Windows. The scripts use .NET Framework reflection APIs; use Windows PowerShell rather than PowerShell 7. |
| Permissions | Write access to the `oshelp` folder for build outputs, caches, and profiles. Game assets and saves are read only. |

Git is optional if you download a source archive. There are no third-party NuGet packages. Package feeds are disabled in `NuGet.config`; builds use the reference packs installed with the .NET 8 SDK.

## Build and run

Place this project in an `oshelp` folder directly inside the game's installation folder:

```text
Obenseuer/
  Obenseuer.exe
  Obenseuer_Data/
  oshelp/
    README.md
    OSHelp.sln
    Launch.cmd
    tools/
```

Open PowerShell in `oshelp` and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Deploy.ps1
.\Launch.cmd
```

This builds the source, prepares missing verification data from your game installation, runs regression and GUI checks, and installs a local build under `app/`. It does not upload or distribute anything. `Launch.cmd` subsequently opens that build; on a fresh checkout it can also perform the initial build for you.

The first build and asset import can take a few minutes. Subsequent launches reuse cached data and verified hashes, checking file metadata and the Steam build for changes. **Data → Refresh game catalog** forces full content verification and extraction.

For a development preview:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
.\Preview.cmd
```

Previews use separate profiles. Run `Deploy.ps1` again to rebuild the selected local release. An existing window can stay open during a build; relaunch to use the update. `Deploy.ps1 -Rollback` selects the preceding local build. The execution-policy option applies only to the PowerShell process being started and does not change system policy.

## Start a playthrough

1. Choose **Import save**, select a character and slot, and review the state and stock sources. Existing profile names require overwrite confirmation.
2. Use **Tenants**, **Upgrades**, and **State** to inspect requirements and record plans. Confirm renovations you still need to build; **Shopping Cart** totals their materials.
3. Use **Sync latest save** after playing to update the profile from its character's newest manual, automatic, or quick save. The profile name and pending goals are preserved. A manual profile can choose its character on first sync.
4. Use **Vendors** to choose an in-game weekday and time. Hours account for known entrance locks; quest-dependent access appears in the details.

Profiles autosave. **Delete** asks for confirmation and keeps a recovery copy. Deleting the last profile creates an empty playthrough. Game saves are never modified.

## Your data

| Data | Location |
| --- | --- |
| Normal profiles and caches | `local/userdata/` |
| Preview profiles and caches | `local/preview-userdata/` |
| Deleted profile recovery copies | `local/userdata/profiles/deleted/` |
| Previous profile contents | The corresponding profile's `.json.bak` file |
| Default game saves | `%USERPROFILE%\AppData\LocalLow\Loiste Interactive\Obenseuer\Saves\` |

You can browse to a custom save location. Back up `local/userdata` before moving or removing the project. Recover an archived profile through **Import profile**.

Local profiles, exports, and diagnostics can contain your character names and save locations. They are excluded from this source repository; review any files you choose to share separately.

## Verification and development

Re-extract game data and run regression and desktop checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1 -Verify -Smoke
```

After verification data has been prepared, run the regression suite directly:

```powershell
dotnet run --project .\tests\OSHelp.Verify -c Release
```

Check release sources for compiled artifacts and machine-specific paths:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-SourceRelease.ps1
```

Build outputs, caches, profiles, saves, and machine-specific captures are ignored by Git. OSHelp uses Windows Forms and standard .NET libraries; the read-only Unity asset reader is included as source. Decompiled game source is not needed to build or run the app.

See the [user guide](docs/prototype.md), [save import and sync guide](docs/save-import.md), [vendor guide](docs/vendors.md), and [implementation notes](docs/implementation.md). OSHelp is an independent companion project and is not affiliated with the game's developer.
