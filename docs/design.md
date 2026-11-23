# OSHelp application design

The implemented **Tenement Planner** is an offline Windows desktop host for a tenant reference, individual renovation bills, and a persistent shopping cart. It reads the installation and writes its own profiles/cache. Its [usage guide](prototype.md) describes the delivered behavior.

## Platform

Use C# and Windows Forms with no third-party packages. The prototype targets `net8.0-windows` using the available .NET 8 SDK and explicitly rolls forward to the installed .NET 10 desktop runtime. This combination has been exercised on the user's Windows 10 host. A later .NET 10 target requires its SDK; that installation is unnecessary for this prototype.

The UI uses [Obenseuer-inspired colors](visual-style.md): charcoal/olive surfaces, cream text, ochre actions, blue navigation, and typewriter headings. [Theme tokens](../design/obenseuer-dark.json) are shared. Grids, editors, selectors, scrolling surfaces, menus, and app-owned dialogs are explicitly dark. Small custom controls wrap ordinary Windows Forms controls where native styling fails on this host. Window chrome and OS file pickers remain managed by Windows. Per-monitor DPI awareness is declared; the current host is the tested configuration.

## Architecture

| Component | Responsibility |
|---|---|
| `OSHelp.App` | Desktop host, profiles, Tenants, Upgrades, Shopping Cart, State, Data, theme, dialogs |
| `OSHelp.Core` | Catalog import/validation, models, verified rules, dependency planner, atomic persistence |
| `OSHelp.Verify` | Standalone regression runner without test framework dependencies |
| `tools` | Read-only Unity/IL extraction, scene/dialogue/UI joins, build and GUI verification |

The prototype reuses the proven Windows PowerShell 5.1 metadata reader through hidden child processes. It does not execute game code. A pure C# metadata adapter remains a possible later simplification.

## Data and correctness

The immutable catalog contains residents, households, requirement fields, upgrades, material IDs, explicit prerequisite edges, apartment groups/instances, contractor limits, difficulty presets, sources, recruitment conversations, and hashes. Stable asset paths identify upgrades/residents; namespace and numeric ID identify materials. Asset names are retained as evidence rather than used to guess applicability.

Profiles contain apartment/building levels, occupancy/readiness, permission/recruitment facts, difficulty multipliers, current stock, confirmed goals, and job history. Unrecorded state is nullable. Each profile is bound to its catalog fingerprint.

Startup hashes the eight planning inputs and verifies the supported game assembly. A changed fingerprint triggers staged extraction, exact byte-boundary/reference validation, and atomic cache replacement. Tenant-location source hashes are checked separately and scene placements are retraced when those inputs change. This additive reference data preserves existing profile/cart fingerprints. Unsupported game code fails explicitly. The baseline full scan identifies the current source files; it does not promise compatibility with arbitrary future layouts.

Rental evaluation follows the installed requirement order and compares actual enum levels. Full eligibility includes rental UI availability, player-apartment exclusion, occupancy, readiness/renovation restrictions, the blueprint item, recruitment facts, and whether the resident already rents. The Shop has no rental UI and cannot be assigned a tenant, while retaining its renovation definitions.

Recruitment is joined from actual `StartRent` callbacks and relevant complete dialogue conversations. Save import confirms recruitment for current tenants and reads the resident actor's renovation permission. Unrented applicants' quest eligibility is recorded manually; the importer does not execute dialogue scripts. The right tenant panel calculates apartment-specific gross payments using actual current upgrade values, size scaling, currency-specific base income, float32 truncation, the rental-income multiplier, payment frequency, and game rounding. It displays the current renter, replacement payments, separate OC/RM changes, and a monthly reference. See [rental income rules](rental-income.md).

The tenant list has separate rental-status and offer-location columns. Status comes from actual profile occupancy and excludes furniture-only placements and non-rental apartments. Location hints join resident NPC IDs to scene `NPCInfo.character` references and BuildSettings scene names, excluding Test hierarchies. The list is widened and wraps status/location text; both fields are searchable and repeated in details.

## Planning and cart semantics

Users review proposed work and confirm outstanding construction before adding it. The planner traverses explicit AND prerequisites, deduplicates shared building work, and scopes apartment jobs by number. It does not assume every intervening tier is mandatory. Recorded facility levels satisfy prerequisites. Tiers with no capable contractor cannot become free construction merely because their bills are empty.

Tenant planning proposes the lowest applicable constructible tier meeting each requirement. Alternatives can be selected in Upgrades. Plans remain separate from current facilities and recruitment gates.

Costs use game float32 multiplication, integer truncation, and minimum-one conversion per material line before summation. Current stock is subtracted once after aggregation. Material, money, and speed settings are independent, including the separate ignore-costs bypass. Nonfinite settings are excluded pending Mono conversion parity work.

Jobs are Planned, Under construction, or Completed. Starting new work consumes recorded stock once; recording already underway work does not consume stock again. Completion updates levels and preserves history. Currency/time are references without wallet or timer simulation.

## Persistence

Each mutation clones the profile, recalculates, saves atomically, then updates the UI. Failed writes retain the previous visible state. Backups, revision checks, and profile leases protect against corruption or competing writers. OSHelp JSON import creates a new identity; game-save reimports use the same profile identity after overwrite confirmation. Schema/catalog mismatches fail explicitly. The selected profile is remembered separately.

Save import resolves JSON object references and matches upgrades by saved asset paths. New names create profiles; existing names require overwrite confirmation. Name matching ignores case and outer whitespace. Confirmation binds to the existing profile hashes/revisions; changed or locked profiles cannot be silently overwritten. All matching profiles are leased before committing. Extra legacy duplicates are archived, with rollback if replacement fails. Active work and final-payment status stay separate from facility levels. Users select inventory/bag/contractor stock sources. Provenance is retained and game saves are never modified. See the [save adapter design](save-import.md).
