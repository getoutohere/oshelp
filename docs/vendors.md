# Vendors and operating hours

Open **Vendors** to browse one row per shop, with a single set of **Opening hours** that already accounts for entrance locks. Checkout, furniture, and dialogue menus for the same business are grouped together. Alternate stock menus and exterior display copies do not create extra rows. Separate store branches remain distinct locations; matching self-service dispensers at the same location are grouped.

Search names, operators, locations, or shop types. Filter by region and by staffed shops/self-service machines. Choose an **in-game weekday and 24-hour time**; this does not use the Windows clock. The weekly table also lets you select the day. **Open only** filters by opening hours.

Click a column heading to sort. Both vendor tables retain the chosen column and ascending/descending direction when the day, time, or filters change, including searches with no matches. The selected vendor stays selected while it remains visible.

The list shows the vendor, operator, location, opening hours, and **Open / Closed** status. The weekly table shows just the day and opening hours. Shops with game-progress requirements show **Open (conditional)**, **Check requirements**, or **Unavailable** as appropriate.

For the **Bazaar General Trader**, Saturday shows **08:00–12:00**. At **13:30** it reports **Closed**. Its checkout and furniture menus appear together, and the furniture menu cannot extend its staffed opening hours. An unused background copy of the building's door is excluded.

The **Hardware Stall** on Trade Road is operated by **Henning Torskvær** and opens **Tuesday 12:00–18:00**. Its storage label and declared Hardware/Tools categories establish the displayed name; its original trade-component title, **Henning's shop**, remains searchable. Labels are joined by the actual shared GameObject, with category checks to reject copied labels on unrelated stalls.

The detail panel names the operator and explains any access requirement. **Source details** retains the individual menu definitions, underlying schedules, scene/object identities, relay callback chains, and file hashes. This is a schedule reference; it does not read a live game clock or simulate quest state.


## Extraction and calculation

`tools/Export-Vendors.ps1` scans all **250** top-level Unity `.assets` and `levelN` files. `tools/Export-VendorExtras.ps1` reads the additional dialogue menus, visibility/state controllers, and dialogue database. `tools/Export-VendorLabels.ps1` reads the public storage labels. These inspect serialized data and reflection-only metadata without executing game code. The [catalog](evidence/vendors.json) retains **253 SHA-256 source hashes**, including the scene list and both game assemblies.

The extraction retains 135 menu/placement records for provenance. `VendorDirectory` groups those into 96 displayed shop/dispenser entries using the business name, operator, and actual scene. A shop's primary staffed menu sets its timetable; its entrance and physical shop-door restrictions still apply. Inactive memory copies do not replace active shops, and an exterior display copy cannot bypass the storefront's door schedule.

The importer supports `Trade`, `TradeSpecial`, `MarketShop`, `FurnitureShop`, `TrainShop`, and `VendingMachine`. It resolves compiled NPC identities, `InteractableTalk` → `RelayDialogue` → actual menu targets, `RelayWeekdays`, `Logic_openingTimes`, `DoorSchedule`, and chained Relay outputs. GameObjectStateSave activation and FadeGameObject visibility can constrain otherwise untimed shops. Operator dialogue overrides shared stock ownership when appropriate; Anneli and Jooseppi's Bar Bizarre menus consequently have the correct separate operators.

Clock-driven effects are applied only when open and closed callbacks resolve to opposing actions on the **same target**. Door locks are joined by their actual target and scene destination. Named internal shop groups provide additional physical-door joins. Nearby schedules are not automatically assigned to unrelated vendors. Other timed internal doors remain individually inspectable. Arbitrary walking routes through a multiroom interior are not simulated.

`OpeningTimes` uses Monday=0, inclusive opening, exclusive closing, and equal hours as closed. Its overnight check uses the *current weekday's* configured pair: Monday 22–06 means Monday 00–06 and 22–24, independently of Tuesday's setting. `DoorSchedule` uses explicit weekday/start/end entries and does not wrap overnight. Required schedules are intersected and alternative actor/entrance paths are unioned. The view accounts for controller activation/quest conditions and player-distance delays without executing them.

The legacy output layout is now decoded through the end of each supported complete record. The current assembly's `Outputs.outputCustomEvent` field is absent from these saved scenes; treating it as serialized was the earlier relay-reader error. Delegates are also omitted from Unity serialization. Prefix-only readers explicitly stop at the last required field and check their record boundary.

Vendor metadata is independent of the renovation catalog fingerprint. Startup checks the complete scanned file inventory and reuses verified source hashes when file metadata and the Steam build match. Changed inputs are hashed again; changed content or inventory triggers extraction. **Data → Refresh game catalog** forces full content verification. Existing profiles and shopping carts keep their identities and quantities.

## Verification

The regression suite covers the General Trader's complete week and Saturday 13:30, exact boundaries, bakery door hours, Teuvo's actor timetable, alternate Mr. Bag actors, the internal furniture-shop door, separate Bar Bizarre operators, test/inactive placements, missing key/quest schedules, and overnight/union/intersection semantics. GUI verification exercises search, region/type filters, invalid time, the access-only filter, day selection, and the Saturday closed-door case alongside existing planner/save-import checks.

To re-extract the raw evidence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Export-Vendors.ps1
dotnet run --project tests/OSHelp.Verify -c Release -- --build-vendors
```

This writes raw evidence and a regenerated catalog under `local/vendor-research/`; it does not overwrite the reviewed documentation baseline. See the original [General Trader investigation](vendor-hours-investigation.md) for the door and relay identities.
