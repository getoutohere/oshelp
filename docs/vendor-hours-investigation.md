# Vendor opening hours: investigation and OSHelp proposal

The Bazaar General Trader has **two independent schedules**: its exterior door and its interior Trade component. Reading the trader alone gives the wrong answer for when the player can enter. This was established from installed assets and IL, without running game code.

## How the result was obtained

1. Read Unity BuildSettings scene paths from `Obenseuer_Data/globalgamemanagers`, then map scene indices to `levelN`. The relevant scenes are `level112` = Open Sewer Bazaar and `level77` = Interior Bazaar General Store.
2. Resolve each MonoBehaviour's script pointer through `globalgamemanagers.assets` MonoScript **class names**. MonoScript asset names are not always class names.
3. Find the exterior `DoorChangelevel` whose `OtherLevel` is `Interior Bazaar General Store`: object **305626**, GameObject **76663**, under `Door_001_usable_changelevel → Trader Open → Bazaar_Shop_Generalstore`.
4. Follow its GameObject/Transform ancestry and inspect the shop's logic components. `RelayWeekdays` object **283938**, on GameObject **10045** (`Trader Open`), contains the actual entrance `openingTimes`. Its serialized record was decoded to its exact byte boundary.
5. Follow `onOpen` to Relay **283937**, whose UnityEvent calls **UnLock** on door **305626**, and `onClosed` to Relay **283936**, whose UnityEvent calls **Lock** on that same door.
6. Inspect interior Trade object **40294**, titled General Trader and owned by Ivan Kapustanov (NPC ID 3). Its separate `autoOpenClose` schedule is 08:00–18:00 daily.
7. Check IL: `RelayWeekdays.Check/ForceCheck` applies its OpeningTimes and invokes those callbacks; `Trade.DeltaSeconds` separately applies its own OpeningTimes. `OpeningTimes.IsOpen` maps Monday=0 through Sunday=6. Opening is inclusive and closing exclusive. Schedules with equal start/end mean closed; overnight intervals wrap across midnight.

## Verified result

| Day | Entrance unlocked | Overlap with trader's schedule |
|---|---|---|
| Monday | 07:00–15:00 | 08:00–15:00 |
| Tuesday | 10:00–20:00 | 10:00–18:00 |
| Wednesday | 10:00–14:00 | 10:00–14:00 |
| Thursday | 06:00–17:00 | 08:00–17:00 |
| Friday | 13:00–21:00 | 13:00–18:00 |
| Saturday | 07:00–12:00 | 08:00–12:00 |
| Sunday | 13:00–18:00 | 13:00–18:00 |

Saturday at 13:30 is outside the entrance schedule, regardless of the trader's 08:00–18:00 setting. The overlap column describes normal scheduled access and trading, not a guarantee that quest state, actor availability, or other locks permit interaction.

## Reproduce and inspect

The retained research harness is [tools/Inspect-BazaarStore.ps1](../tools/Inspect-BazaarStore.ps1). Run it from `oshelp` using Windows PowerShell 5.1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Inspect-BazaarStore.ps1 > local/bazaar-store-rules.txt
```

It uses `tools/Inspect-Obenseuer.ps1` and `UnitySerializedFile.cs`. Outputs under `local/bazaar-store/` include `records.json`, `shop-logic.json`, and `inputs.json`; the IL is in `local/bazaar-store-rules.txt`. The manually assembled [verified reference](evidence/bazaar-general-trader-hours.json), [input hashes](evidence/vendor-hours-inputs.json), and [inspected IL](evidence/vendor-hours-rules.il.txt) are retained in documentation. The harness's scene/object IDs apply to this installed build and must be rediscovered when those inputs change.

The original research harness read Trade and DoorChangelevel only through the required fields, and misread a later legacy custom-event field in Relay. Its first UnityEvent callback prefixes established the Lock/UnLock links; the later garbled fields were not evidence. The production vendor exporter now handles that legacy layout and validates complete relay records: `Outputs.outputCustomEvent` is absent from these serialized scenes. See the [implemented vendor module](vendors.md).

## Proposed OSHelp vendor module

- Catalog vendors by scene/object identity, title, owner NPC, and source hashes. Keep trader and entrance schedules separate; don't identify vendors solely by display name.
- Trace entrance destinations and the controlling callback graph, including RelayWeekdays, DoorSchedule, Logic_openingTimes, active/inactive object switching, and quest overrides. Confirm the referenced target before applying a nearby schedule to a vendor.
- Show **entrance hours**, **trader hours**, and their calculated overlap, with explicit closed/overnight handling. Offer source details and distinguish configured schedules from imported live lock/open state.
- Include distance-delayed transitions and update cadence when explaining boundary behavior: the observed door controller checks each game minute, and both controllers expose player-distance checks.
- Validate complete supported records, references, weekday numbering, and input fingerprints. Test Saturday 13:30, exact opening/closing boundaries, equal-hour closure, overnight windows, mismatched entrance/trader hours, multiple entrances, and quest-controlled access.

The [Vendors view](vendors.md) now groups each shop's menus into one row and shows combined opening hours, with time checking, source inspection, and regression tests. The underlying trader/door schedules remain available in source details. The General Store's unused background door in the Tenement scene is excluded. Arbitrary navigation through multiroom interiors and live quest/lock simulation remain outside the schedule reference.
