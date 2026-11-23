# Using the prototype

Run [Launch.cmd](../Launch.cmd) or [OSHelp.exe](../app/OSHelp.exe). Startup checks game file metadata and reuses previously verified hashes; changed files are hashed again. The first validation or a cold catalog import takes longer. **Data → Refresh game catalog** forces full content verification and extraction.

## Record your playthrough

Use **New**, **Rename**, and the profile selector for separate playthroughs. The last selected profile reopens automatically. **Import profile** accepts exported OSHelp JSON; **Export** makes a portable copy.

**Delete** asks for confirmation, with Cancel selected by default. The profile and its backup move to `profiles/deleted`, outside the profile list; **Import profile** can recover the archived JSON. Another available profile is selected, or an empty playthrough is created when the last profile is deleted.

**Sync latest save** updates the selected profile from its character's newest manual, automatic, or quick save. The profile name and identity stay the same. Shopping goals are retained and recalculated: completed work drops out, underway work is not charged materials again, and future work stays planned. Selected material stock sources are refreshed too. A manually created profile first asks you to choose its character and stock sources. Sync retains the previous profile as a `.bak`; using an older save requires confirmation.

**Import save** discovers your Obenseuer characters and save slots, newest first. Select a slot, choose **Review selected save**, select stock sources, then choose **Import selected state**. An existing profile name prompts **Overwrite** or **Cancel**. Confirmed overwrite replaces its state and shopping cart, keeps a backup, and archives any older duplicate entries; cancelling changes nothing. **Browse for Info.tnmt** supports saves elsewhere. Imported state includes apartment/building levels, occupancy, clearing, blueprint permission, difficulty, active renovations, and material stock. Work awaiting final payment remains unfinished. See [save import details](save-import.md).

In **State**, select an apartment and record its current facility levels. Enable **Building facilities** for shared levels. Record occupancy, the Tenement blueprint permission item, and apartment readiness/renovation permission. **New-game defaults** explicitly replaces levels and occupancy with the installed scene's starting values; use it only when those match your game.

Select your difficulty preset, then **Apply settings**. Materials, money, and speed are independent. Casual comes from game data: 0.5 material cost, 0.5 money cost, and 2 construction speed. Custom finite values are supported within the displayed limits. The separate creative/debug bypass removes costs entirely.

State can be imported or entered manually. Use **Sync latest save** to pick up subsequent game changes; synchronization runs when requested. A different imported profile name creates another entry; the same name requires overwrite confirmation. Blank facts are unrecorded; confirming a plan does not mark facilities as built. Game saves are never modified.

## Find tenants and renovations

**Tenants** includes all resident definitions. Select a resident/apartment for every required facility, recorded level, unmet conditions, and the game's first facility rejection. Recruitment eligibility and landlord permission are separate gates. Open the source/recruitment details to read extracted dialogue text, conditions, scripts, and callbacks. Record recruitment eligibility when those conditions are met in your game.

**Rental status** marks current tenants as **Already renting**, with their apartment number, using the selected profile's imported or recorded occupancy. Furniture-only placements are excluded. A blank playthrough shows **Not recorded** until occupancy is known. **Offer location** gives the recruitment area from actual scene NPC placements; search also matches locations and rental status. The detail panel repeats both fields, and Source + recruitment includes the scene/object evidence. These are recruitment locations; a recruited tenant may now be living in your building. See [tenant location evidence](tenant-locations.md).

The right panel names the **selected apartment's current renter** and compares **actual rent payments**, **payments with the selected resident**, and the **change in OC and RM**. Amounts include the payment period; monthly totals are shown separately. Gross rent includes the apartment's upgrade value, rental-income multiplier, payment frequency, and game rounding. Vacant/furniture-only units earn zero. Moving-in renters earn zero until their move finishes; the proposed replacement assumes move-in is complete. Eligibility remains listed below. See [the verified calculation](rental-income.md).

Import reads the rental-income multiplier, payment schedule, and move-in state. Existing imported profiles recover missing facts automatically when their original save snapshot still matches its recorded hash. For manual profiles, **State → Rental income + payment schedule** records both rent settings, and **Moving in** records an unfinished move. Applying Default or Casual also applies that preset's rent settings; changing renovation costs alone does not define custom rental income.

The Shop and player apartment are excluded from tenant placement. The Shop remains selectable for renovation reference/planning.

**Plan missing upgrades** chooses the lowest applicable constructible tier meeting each unmet facility requirement. Review the proposed jobs and prerequisites before confirming. Choose another tier directly in **Upgrades**. A facility plan does not complete recruitment quests or grant landlord permission.

**Upgrades** shows apartment or building jobs. **All definitions** includes starting states, inapplicable records, and tiers without a capable contractor, with availability explained. Details contain full bills, prerequisites, contractors, scaled prices/time, and raw source references.

## Keep a shopping cart

Add renovations from Tenants or Upgrades, review the work, and confirm it still needs construction. Explicit prerequisite jobs are included. Shared building work is counted once; apartment jobs stay separate.

In **Shopping Cart**, edit **Have** to record current unspent stock. Stock is subtracted once from the combined requirement. Select a material for contributing jobs. Quantities use the game's per-line float multiplication, truncation, and minimum-one rule before aggregation.

- **Start work** records new construction and consumes its bill from recorded stock once. Record completed prerequisites first.
- **Already underway** records construction started before tracking it in OSHelp. It removes the outstanding bill without consuming current stock again.
- **Complete work** updates facility levels and retains history. It records your confirmation; it does not run a timer or make in-game payments.
- **Remove goal** removes future work and recalculates dependencies. Shared work remains while another goal needs it.
- Export CSV/text. **Record all stock available** explicitly confirms possession of outstanding materials.

Existing completed renovations can be recorded directly in State, removing satisfied work from the plan. Currency totals are reference amounts; the app does not track a wallet.

## Vendors

The **Vendors** view shows one row per shop, grouping duplicate checkout, furniture, and dialogue menus. Choose a game weekday/time, search or filter, and inspect the full week. A single **Opening hours** column combines the staffed timetable with entrance locks. At Saturday 13:30, the Bazaar General Trader correctly reports **Closed**. The detail panel explains dialogue access and game-progress requirements; source schedules are available under **Source details**. See [the vendor guide](vendors.md).

## Data and persistence

The **Data** page shows source hashes, counts, runtime, and refresh. Source details retain raw fields/pointers; recruitment excerpts include relevant complete conversations and earlier conditions.

Default storage is `oshelp/local/userdata`: `catalog.json`, `settings.json`, and `profiles/*.json`. Writes atomically replace files and retain a `.bak`. Profile leases/revisions prevent competing writers from overwriting changes. Backup recovery requires confirmation. Export profiles before moving/deleting this repository.

Use **Launch.cmd** or **app/OSHelp.exe** for the stable release and **Preview.cmd** for development. Previews keep separate profiles under `local/preview-userdata`. Normal builds leave the stable release untouched. `tools/Deploy.ps1` publishes a tested, complete release directory with its tools/theme/evidence snapshot and switches the stable launcher to it. Existing windows keep running their original version. `tools/Deploy.ps1 -Rollback` selects the preceding version for new launches.

Arguments: `--game <game directory>`, `--repository <support-data directory>`, `--data-dir <profile/cache directory>`. The stable launcher supplies the release snapshot and existing normal profile directory. Each release also records these defaults in `release.json` for direct launches of its executable.

The prototype supports the inspected installed game assembly. Changed game code stops evaluation until reviewed. Changed catalog fingerprints preserve previous profiles and require a separate current-catalog profile rather than silently migrating quantities. Imported profiles retain the slot path, saved date, game version, selected stock sources, and source hashes under **Data → Imported save details**.

Built using Windows Forms, standard .NET libraries, and Windows PowerShell; verified on this Windows 10 machine with .NET 10.0.9. Automated recruitment quest evaluation, construction timers, wallet/payment simulation, and installers are future extensions.
