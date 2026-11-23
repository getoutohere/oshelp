# Tenant status and offer locations

The tenant table displays **Already renting** and the apartment number for each actual tenant in the current profile. This uses imported or recorded occupancy rather than recruitment flags. Furniture-only placements, the Shop, and the player apartment do not count. With incomplete occupancy records, other residents show **Not recorded**; a fully recorded profile can establish **Not renting**.

**Offer location** identifies the area where the resident is placed before recruitment. It remains a recruitment hint after they move into your building. Names, household members, rental status, and locations are searchable. Full location evidence is available through **Source + recruitment**.


## Source mapping

`Export-TenantLocations.ps1` scans all 250 top-level uncompressed Unity asset/scene files. It joins:

1. Resident definitions' NPC IDs.
2. `NPCInfo.character` pointers to serialized `Character.ID` fields, resolving external asset files.
3. Scene `levelN` files to actual scene paths in `globalgamemanagers` BuildSettings.
4. GameObject/Transform ancestry for each placed NPC.

The resulting [placement records](evidence/tenant-locations.json) cover all 31 resident definitions. Every one of the 29 recruitable residents has a normal scene placement. Some residents also appear under `Test` hierarchies in the Clock Tower or player tenement; these records are retained as evidence and excluded from offer hints. Malone is an initial resident of apartment 7, and Crazy Neighbor has no rental-offer callback, so they have explicit special-case hints.

`NPCBehavior.json` alone is insufficient: many residents use generic prefab paths, and several entries are duplicated or lack a start scene. Prefab folder names are not used as proof of a recruitment location.

Examples established by the scene joins:

| Resident | Offer area |
|---|---|
| Aleksi Kivi | Deekula A |
| Amanda Korhonen | Deekula B |
| Vivi Bondar | Deekula C |
| Sara Claine | Bus in the tenement district |
| Ville Sköldgangster | Speakeasy |
| Fenella Hunt, Kirka Kääbus, Taneli Kurju | Kolhola |
| Jonne Reis | Tenement A |
| Einar Kaivuri | Mines canteen |
| Harri Hyvinkää | Bazaar Fungi Boutique |
| Elsa Varis | Bazaar internet café |
| Per Otter | Bazaar inn |
| Urpo Murhapuro | Bazaar Murju A |
| Veeti Pärisevä | Bazaar bar |

The outdoor residents are labelled Tenement district or Bazaar outdoors according to their actual scene placement. No exact daily timetable or current live position is implied by this area hint.

## Validation and cache compatibility

The [24 location input hashes](evidence/tenant-location-inputs.json) include the assembly, BuildSettings, MonoScript definitions, and relevant scene/character assets. Startup verifies them and retraces placements if they change. Location metadata is independent of the planning fingerprint, so this update preserves existing profiles, imported saves, and shopping carts.

The reader bounds partial `Character`/`NPCInfo` field reads and checks complete `info_NPCSpawn` records when encountered. It does not claim complete NPCInfo deserialization. Rental annotations are tested against vacant, unrecorded, actual-renter, furniture-only, and non-rental states. GUI verification changes occupancy and searches by a scene-derived location before capturing an imported profile's tenant list.
