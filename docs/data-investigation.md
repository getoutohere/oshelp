# Obenseuer tenant and renovation data investigation

Investigated the installed game on 23 November 2026. The authoritative inputs for this tool are the game's serialized resident and upgrade definitions, the item database, and the code that evaluates them. Reading `CharactersRent.json` alone would produce an incomplete and incorrect tenant catalog.

## Verified inventory

The investigation scanned **250 uncompressed Unity serialized asset and level files**, all reporting Unity **6000.0.77f1**, serialized format **22**, little-endian, with stripped type trees. It found:

| Definition | Count | Location |
|---|---:|---|
| `TenementResident` | 31 | `sharedassets0.assets` |
| `TenementUpgrade` | 131 | 130 in `resources.assets`; one in `sharedassets0.assets` |
| `TenementUpgradeGroup` | 25 | `resources.assets` |
| `TenementApartmentType` | 5 | `resources.assets` |
| `TenementContractor` | 4 | `sharedassets0.assets` |

All 196 decoded objects consumed their serialized byte ranges exactly. All 131 upgrades are referenced by an extracted upgrade group. All prerequisite and group references resolve. The **662 material lines** resolve to IDs in `StreamingAssets/Items.json`. Of the 131 upgrade definitions, 93 have materials or a nonzero monetary price. The other 38 include 25 starting states and 13 higher-tier definitions with empty bills and zero prices. An empty bill does not establish that a job is available or free to construct: contractor and UI eligibility still apply. These figures are a fingerprint of this installation, not constants to hard-code into the application.

Evidence: [scan manifest and SHA-256 hashes](evidence/manifest.json), [catalog](evidence/catalog.json), [validation results](evidence/validation.json), [resident table](evidence/resident-requirements.md), and [upgrade table](evidence/upgrade-materials.md).

## Source responsibilities

When investigating game logic, inspect a decompilation of your own installed assemblies if available. Relevant classes include `TenementApartment`, `DifficultyInfo`, `Trade`, `OpeningTimes`, and `RelayWeekdays`. Decompiled game source is not distributed with OSHelp and is not required to build or run it. Inspect IL selectively when precise conversion/rounding behavior or ambiguous decompilation requires it. The installed DLL hashes and serialized assets remain the authority for supported code and object-specific settings; source field initializers are defaults, not proof of scene values.

| Input relative to `Obenseuer_Data` | Responsibility |
|---|---|
| `Managed/Assembly-CSharp.dll` | Serialized field definitions, enums, acceptance checks, prerequisites, difficulty arithmetic, contractor limits, rental UI gates |
| `globalgamemanagers.assets` | `MonoScript` records that associate serialized objects with their game classes |
| `resources.assets` | Upgrade groups, apartment types, most upgrade definitions |
| `sharedassets0.assets` | Residents, contractors, remaining upgrade definition |
| `StreamingAssets/Items.json` | Material IDs and current display names |
| Scene files and dialogue database assets | Apartment instances, configured upgrade groups, existing residents, recruitment and quest conditions |
| `StreamingAssets/CharactersRent.json` | Older rental schema; retain for diagnostics, not as the current requirement catalog |

`CharactersRent.json` contains only 11 rows and assigns ID 24 to both Oscar Nilsson and Amanda Korhonen. Amanda's compiled resident definition instead uses NPC ID 42 and has specific electricity, water, heating, bathroom, wall, and kitchen requirements. The current `TenementResident.CheckRequirements` method reads those compiled fields directly. The older `CharacterRentDatabase` still contains JSON-loading code; its presence does not make its data authoritative for the current rental checks.

## Rental requirements established from code

The [IL evidence](evidence/rules.il.txt) records method names, metadata tokens, branch targets, and resolved operands from the installed assembly.

`TenementResident.CheckRequirements(TenementApartment)`, token `0x06002A52`, checks these conditions in order:

1. The apartment's Small, Medium, or Large size is accepted by the corresponding resident boolean.
2. The apartment is not occupied by another actual resident. `onlyFurniture` is an explicit exception in this check.
3. Apartment electricity, water, heating, bathroom, walls, front balcony, and kitchen meet the resident's minimum enum values.
4. Building elevator, stairs, basement, basement sauna, facade, courtyard, and mine meet the resident's minimum enum values.

The method returns the first rejection category: Size, Resident, Apartment, or Tenement, or None when these checks pass. Its facility comparisons use ordered enum values and `>=`, not names or an overall apartment-condition label. The rear balcony is a separate upgrade category, but this acceptance method checks `currentBalcony`, not `currentBalcony2`.

`ApartmentRentUI.ShowRentButton` separately rejects apartment number **1** as the player's apartment before calling `CheckRequirements`. This is why a tool must reproduce the full decision path rather than a convenient-looking requirement method alone.

`TenementController.CanRentApartment` checks possession of the configured task item and whether that NPC already rents an apartment. Recruitment dialogue conditions and the actual task-item reference must also be traced when implementing a full current-playthrough eligibility result.

`TenementApartment.IsApartmentAvailable` is another distinct check used in apartment handling. Its path includes examined/cleared status, current upgrades or construction, existing residents, and upgrade restrictions. Where the clearing branch applies, it requires at least **90% primary** and **80% secondary** furniture clearance. These gates must be shown separately from the resident's preferences and applied at the same call sites as the game.

### Resident identity and special cases

Each resident has a persistent `addressableAssetPath`, primary NPC reference, other resident references, base OC/RM income, eviction and renovation flags, and building/apartment minimums. Use the persistent path as catalog identity and the NPC ID for NPC joins. Do not deduplicate households by a display name.

The 31 definitions include **Malone Petronis** and **Crazy Neighbor**, both with eviction disabled. Crazy Neighbor has `dialogueActor = Resident/Crazy Neighbor`. `CantUpgradeApartment()` consults that actor's `CanUpgradeApartment` dialogue field when an actor is configured; otherwise it uses the serialized boolean. These are concrete examples of static data requiring live story state for a playthrough-specific result.

The research catalog deliberately includes every resident definition. Classifying each as recruitable, already resident, or story-restricted requires scene assignments and recruitment dialogue links. That classification is an implementation acceptance requirement, not an inference from the count of asset definitions.

### Checked examples

| Resident | Requirements from current serialized definition |
|---|---|
| Aleksi Kivi | Small apartment; Makeshift bathroom and walls |
| Amanda Korhonen | Medium apartment; electricity On, Dirty water, Shabby heating/bathroom/kitchen, Makeshift walls; household includes Tea Henderson |
| Kirka Kääbus | Medium or Large apartment; electricity On, Good water, Decent heating/bathroom/walls/kitchen; Makeshift stairs, Shabby basement and courtyard, Decent basement sauna; household includes Hall Kääbus |

All other fields and households are in the [complete resident table](evidence/resident-requirements.md). The catalog preserves raw enums; human-facing names should be mapped separately.

### Income is not a fixed rental quote

`GetMonthlyRent` adds `incomeOc`/`incomeRm` to `apartment.GetScaledValue()`, but the actual recurring income uses `GetRawOcIncome`/`GetRawRmIncome`. Those methods only add an apartment bonus in a currency whose tenant base income is nonzero, truncate before applying the rental-income difficulty multiplier, and truncate again afterwards. The right tenant panel now follows that recurring-income path. Divisors, contributing upgrades, size factors, move-in/furniture-only exclusions, and independent difficulty settings are established in the [rental calculation evidence](rental-income.md). The old JSON income columns are not used.

## Renovation definitions

`TenementUpgrade` stores:

- Persistent asset path and an optional `id` field, which is empty in checked records.
- Upgrade value, base construction hours, OC start payment, and RM final payment.
- Exact `requiredItems` entries: item reference, namespace, ID, and integer amount.
- An array of prerequisite upgrade references.
- Flags and target enum values for building and apartment categories, including separate front/rear balconies.

`TenementUpgradeGroup.updateGroup` links selectable definitions. `TenementApartmentType` links the groups for electricity, water, heating, bathroom, walls, balconies, and kitchen. There are five apartment-type records: Small, Medium, Large, Shop, and Player. Medium and Large apartments can reference definitions whose asset names begin with `Small-`; resolve actual group references rather than infer applicability from filenames.

General upgrade groups cover the basement, sauna, mushroom farm, workshop, mine, electricity, water, heating, elevator, stairs, facade, roof, courtyard, and sauna/pier. The schema also has fence and greenhouse flags. A schema field alone does not prove that a selectable upgrade exists: the extracted groups and scene/controller links establish availability. Player extensions and shop-specific bathroom upgrades must be retained.

`TenementUpgrade.CanUpgrade` requires **all** entries in `requiredUpgrades` to satisfy `IsUpgraded`. `IsUpgraded` compares current levels against the requested category/tier. This is an explicit dependency graph, not a blanket rule that every cheaper tier must first be purchased. The upgrade UI also handles inferior choices, construction in progress, final payments, and contractor capability.

The four contractor definitions are Ken Nakahara, Rob Madsen, Samuel Jonasson, and Urho Siltanen. Their capabilities are per category and must be interpreted through `TenementContractor.CanUpgrade`, including apartment size.

### Difficulty changes actual quantities

`TakeRequiredItemsAndMoney` and `CanUpgrade` call `DifficultyInfo.GetRenovationResourceCost` **for each material entry**. The observed calculation for finite settings is:

```text
scaled = float32(baseAmount) * float32(resourceMultiplier)
required = max(1, truncateTowardZeroToInt32(scaled))
```

Apply that conversion before summing lines, upgrades, or apartment instances. For example, two separate base quantities of 3 at a 0.5 multiplier require 1 + 1 = **2**, not truncate(6 × 0.5) = 3. Even a Disabled/zero resource multiplier reaches the minimum-one branch in this method. A separate controller flag, `ignoreUpgradePricesAndItems`, bypasses consumption entirely and must not be conflated with that multiplier.

`DefaultMultiplier` maps to 0, quarter-step multipliers from 0.25 through 4, Default = 1, and Infinite. Infinite and integer overflow need explicit Mono conversion parity work before being exposed as supported cart settings; they must not be silently evaluated with unrelated .NET behavior. The first implementation should support the verified finite settings and record the precise selected setting.

Start and final prices use a separate renovation-cost multiplier and integer conversion. Construction time divides base hours by a renovation-speed multiplier and then converts to integer. These settings must remain separate.

### Concrete bill checked against bytes

At resource multiplier 1, `Small-Bathroom-1-Makeshift` requires **2 Planks, 4 Nails, 5 Scrap Metal, and 5 Tarps**, with a base start payment of **320 OC**, final payment of **20 RM**, and construction time of **8 hours**. It has no explicit prerequisite upgrades.

`Small-Walls-1-Makeshift` requires **30 Planks, 30 Nails, 15 Scrap Metal, 4 Duct Tape, and 2 Tarps**. Selecting both jobs for one compatible apartment therefore gives **32 Planks, 34 Nails, 20 Scrap Metal, 4 Duct Tape, and 7 Tarps** before subtracting stock already owned. The table resolves material names through IDs, so `Scrap metal` and `Scrap Metal` contribute to the same item.

## Extraction implementation demonstrated

The included research reader reads the Unity file header, type/object tables, and external file references. It resolves each target object's script reference through the `MonoScript` catalog, reads the object's name, and decodes serialized fields using the game assembly's field metadata. It does not search for a nearby number in a binary string dump.

The decoder handles inheritance, Unity object pointers, strings, aligned booleans, integer enums, arrays/lists, and the inline reference/value structures used by these definitions. Every decoded object must end at its declared byte boundary. The production reader should use `System.Reflection.Metadata`/`PEReader` for schema inspection rather than require Windows PowerShell's reflection-only API.

The reader is deliberately narrow. It rejects other versions, big-endian files, and files with type trees instead of silently applying the wrong layout. The scan covered the 250 top-level uncompressed `.assets` and `levelN` files. Addressables bundles need additional handling only if a future supported installation places required definitions there. Inspect bundle dependencies during compatibility discovery rather than assume the current layout indefinitely.

## Completed scene and dialogue joins

The prototype imports these joins with `Export-PlanningData.ps1` and `Inspect-ApartmentUi.ps1`, validating complete serialized-object byte consumption.

- The `level2` controller supplies **17 apartment instances**. Apartment 1 is the player's Large apartment, apartment 2 is the Shop, apartment 4 is Small, and Malone is initially assigned to apartment 7. Types and groups are resolved through references.
- The permission item is **Tenement blueprint**, `TaskItemMap` object 125135, enum `TenementBlueprint` value 31.
- The dialogue database contains **35 StartRent callbacks in 29 conversations**, covering 29 resident definitions. Malone and CrazyNeighbor are existing/special definitions. Relevant complete conversations retain earlier conditions and scripts; current quest facts are recorded by the user.
- `level0` supplies Default, Casual, and Custom difficulty records. Default is 1/1/1. Casual is **0.5 materials / 0.5 money / 2 speed**. The multiplier enum has a gap: value 0 means Default=1, 1 means Disabled=0, 2–4 map to 0.25–0.75, and 5–16 map to 1.25–4.

### Shop rental exception

The Shop cannot be rented to tenants. Its `ApartmentInfoUI` object **204415** in `level2`, under `Apartment Info -> Apartment Shop`, has **rentUI.pathId = 0**. Small, Medium, Large, and Player panels have non-null rental references. `ApartmentInfoUI.UpdateApartmentInfo` only calls `ShowRentButton` when that reference exists; `ShowRentButton` separately rejects player apartment 1. The [scene pointers](evidence/apartment-info.json) and [UI method IL](evidence/apartment-ui-rules.il.txt) establish this path. Shop renovation groups remain valid.

### Apartment availability

Availability checks resident renovation restrictions and dialogue-dependent renovation permission. Existing positive apartment upgrades or underway work bypass clearing checks except in the furniture-only case. Otherwise the apartment must be examined, with primary clearing at least **90%** and secondary clearing at least **80%**. Clearing is calculated from current/preplaced item counts, returning 100% for a zero initial count. The prototype accepts the resulting percentages and permission facts as manual state.

The [save importer](save-import.md) now reads saved facility/occupancy/clearing data, active construction/payment status, permission items, difficulty, resident renovation permissions, and selected stock sources. Recruitment quests for unrented applicants remain explicitly recorded facts.
