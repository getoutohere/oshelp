# OSHelp implementation record

The prototype is published as immutable versions under `app/releases`. [Launch.cmd](../Launch.cmd) and the stable `app/OSHelp.exe` launcher start the version selected by `app/current.json`. If no release exists, Launch runs the deployment script. The [prototype guide](prototype.md) covers its workflow.

## Delivered

- Four-project solution: Windows Forms host, core rules/import/persistence, standalone verification runner, and a small stable release launcher.
- Installed-game extraction of 31 residents, 131 upgrades, 25 groups, five apartment types, four contractors, 662 material entries, and 17 actual apartments.
- Recruitment links from 35 callbacks in 29 conversations; existing/special definitions remain inspectable.
- Actual scene difficulty presets, blueprint permission item, starting states, and rental UI exceptions.
- Tenant evaluation, upgrade reference, reviewed multi-apartment plans, prerequisites, persisted stock/cart, job lifecycle, exports, and multiple profiles.
- Obenseuer-inspired dark Windows 10 UI, atomic saves/backups, profile write leases/revision checks, and active-profile persistence.
- Read-only game-save import with character/slot discovery, review, stock-source selection, reference resolution, active/final-payment renovation state, and provenance. Verified against all 24 discovered slots across game versions 0.4.04 through 0.5.19.
- Current rental annotations with apartment numbers, plus scene-derived, searchable offer locations for all 29 recruitable residents. Special residents have explicit hints; test placements are excluded. Location source checks preserve existing profile fingerprints.
- Profile deletion with confirmation/recovery copies and latest-character-save synchronization that preserves shopping goals, reconciles actual renovations, refreshes selected stock, and retains pre-sync backups. Manually entered profiles can link a character on first sync.

## Build decision

The installed SDK is 8.0.407. Build `net8.0`/`net8.0-windows` reference targets with explicit `LatestMajor` runtime roll-forward; the executable is verified on the installed .NET 10.0.9 desktop runtime. No third-party packages are referenced, and `NuGet.config` clears package sources.

The adapter reuses the bounded Unity format-22 reader through Windows PowerShell 5.1. This preserves the demonstrated decoder instead of rushing a duplicate port. Extraction is read-only and child processes are hidden. Outputs are staged and validated before replacing the catalog.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build.ps1 -Verify -Smoke
```

## Verification

Version 0.1.1 corrects Gold Shop and Möbelmann vendor access through naming-based entrance filtering and internal-door association. Vendor cache format 4 causes older extracted vendor catalogs to fall back to the updated, source-validated baseline. This leaves the planning fingerprint and existing profiles unchanged. The [vendor guide](vendors.md#extraction-and-calculation) documents the heuristics and their limits; asset byte decoding and general geometric reachability are unchanged.

Startup validation uses `GameFileValidation`, shared across the four importers. `local/userdata/game-file-hashes.json` stores SHA-256 values with length, UTC modification/creation ticks, absolute game-data root, and Steam build ID. Only metadata is read for unchanged files. A new installation, changed Steam build, missing/malformed cache, or changed metadata causes content hashing. The complete vendor asset-file inventory is still checked, so added/removed scenes trigger extraction. Missing inputs fail validation. Hash reads exclude concurrent writes/replacements, honor cancellation, and compare metadata before/after reading. The cache is saved atomically; profile and save-import integrity hashes still read actual contents.

**Refresh game catalog** creates a session that ignores persisted hash entries, reads every referenced input, and rebuilds extracted catalogs. Shared inputs are hashed once per session. This also verifies edits made while preserving file timestamps. Existing catalog fingerprints and profile/cart identities are unchanged.

`dotnet run --project tests/OSHelp.Verify -c Release -- --benchmark-startup` compares full hashing with a fresh cached session against the installed game, asserts identical catalogs/vendor data and zero content hashing on the cached run, and warms only the normal catalog/hash caches without opening profiles. Measurements are recorded in `local/startup-benchmark.json`. GUI smoke checks also write `startup.json` with data-loading and window-ready timings.

Ordinary builds publish only to `artifacts/preview`. `tools/Deploy.ps1` builds and runs regression verification, publishes a unique release directory, snapshots runtime tools/theme/evidence, and runs GUI checks from that snapshot before atomically updating the release pointer. The launcher defaults to the release's snapshot, actual game directory, and existing `local/userdata`. Its process exits after starting the versioned executable, freeing the canonical launcher for future use. `--wait` is available for automated launcher checks. A release manifest records SHA-256 hashes; `current.json.bak` supports `Deploy.ps1 -Rollback`.

`Preview.cmd` and direct launches of the default preview executable use `local/preview-userdata`. Deployment smoke tests use `local/deploy-smoke-data`; game saves and normal profiles are never copied into releases. A running version's binaries and support files are not overwritten by development or deployment. The first transition from the former in-place binary requires that old application to be closed once.

`tools/Test-Deployment.ps1` verifies this with an owned test window and isolated profiles: it leaves a release running while building a preview and deploying a new version, checks every original release file and the stable launcher hash, exercises the launcher without repository/game overrides, and checks rollback in both directions. Results are retained in [deployment verification](evidence/deployment-verification.json).

The runner checks resident facility thresholds, sizes, occupancy/readiness, Shop/player exclusions, actual recruitment/apartment joins, all material lines at finite settings, concrete combined bills, shared dependencies, apartment scope, stock subtraction, lifecycle transitions, unavailable tiers, and persistence failures/recovery.

The GUI workflow runs the published executable with isolated profiles. It plans Aleksi's Makeshift bathroom/walls in apartment 4, edits stock through the actual grid editor, verifies save/reload and 32 required / 10 owned / 22 remaining Planks, changes a selected facility through State controls, applies Casual through the selector, and verifies Shop rejection through the apartment selector. It also checks vendor search, time/day changes, filters, and the Saturday General Trader entrance lock. It captures all six pages and exits.

Results are in `local/verification-results.txt` and timestamped `local/ui-smoke-*` folders. The build must have no warnings/errors; the runner must pass; the GUI must produce `success.txt` without `error.txt`. The current Windows 10 host/runtime is verified; other OS/DPI configurations have not been exercised here.

Verification includes independent source comparisons for 20 real manual slots and 100 saved apartment valuations. The desktop checks exercise current-renter/replacement revenue, separate OC/RM gains and losses, apartment switching, rental annotations, location search, save import/reload, overwrite confirmation/cancellation, and backups. Persistence tests cover legacy duplicate consolidation, stale approval, concurrent access, failed-write rollback, and recovery of older rental facts only from the exact original source. Recorded results are in [regression verification](evidence/prototype-verification.txt) and [GUI verification](evidence/prototype-gui-verification.txt). Machine-specific screenshots are excluded from the source release.

## Extensions

The delivered scope is deterministic game definitions/rules, save snapshot import, actual rental payment comparison (including schedule and rounding), playthrough tracking, and the [Vendors view](vendors.md) with grouped shop rows and combined opening hours. Automated recruitment quest-script evaluation, game modification, construction timers, wallet/payment simulation, and arbitrary interior pathfinding are outside this prototype. Source dialogue conditions remain visible for the user to assess and record. The [vendor-hours investigation](vendor-hours-investigation.md) records the original door/relay evidence.

Future work can port metadata extraction into C#, target .NET 10 when its SDK is available, add automatic dialogue evaluation, profile reconciliation across snapshots/updates, additional tools, and packaging.
