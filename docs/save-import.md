# Game save import

Click **Import save**, select a character and slot, then **Review selected save**. Choose stock sources and click **Import selected state**. A new name creates a profile. An existing name prompts **Overwrite** or **Cancel**, comparing names without case or surrounding whitespace differences. **Import profile** continues to accept OSHelp JSON exports.

Confirming overwrite replaces that profile's state, stock, cart, construction history, and settings, retaining its identity and the previous contents as a `.bak`. Cancelling leaves existing profiles unchanged. Game saves are never modified.

If older app versions already created duplicate names, confirmed overwrite keeps the active matching profile, or the most recently saved match when another profile is active. Extra duplicates are archived byte-for-byte under `profiles/replaced/<archive-id>/` and removed from the profile selector. They can be recovered through **Import profile**. No existing duplicates are moved until you confirm an overwrite.


The default discovery root is:

```text
%USERPROFILE%\AppData\LocalLow\Loiste Interactive\Obenseuer\Saves\
```

Slots are discovered under character folders and sorted by date. Browse for a slot's `Info.tnmt` or `Globals.tnmt` to import from another location. Save source details remain available under **Data → Imported save details**.

## Imported state

**Sync latest save** uses the imported character identity and character-folder path, independently of the OSHelp profile name. It searches the same save root (including custom locations) for that character's latest slot by the save header date. Filesystem modification times and other character folders are excluded. Manual, automatic, and quick saves all qualify. The source slot may be deleted; other slots for the linked character remain discoverable. Missing or unsupported saves leave the profile unchanged. If the latest available state predates the previous import, Cancel is the default on an explicit older-save confirmation.

Sync replaces imported game facts, difficulty/rental settings, occupancy, active construction and payment state, and material stock. Source selections are persisted by stable IDs; older profiles migrate their recorded source names. Temporarily absent selected bags/depots count as empty and remain selected for future syncs. The current profile identity/name and manually confirmed recruitment facts are retained. Existing goals are reconciled with actual installed levels and active jobs; satisfied goals are removed and the remaining cart is recalculated. Active or completed history is retained only when supported by the latest saved state. No material deductions are replayed. Sync saves in place with revision/lease protection and a `.bak` of the previous profile, without name-based import overwrite or duplicate creation.

For a profile entered manually, the first sync opens the character picker and stock-source review. This links and updates that profile rather than creating another entry. Cancelling the picker or review changes nothing.

**Delete** confirms the selected profile name with Cancel as the default. Its JSON and prior `.bak` are moved to a unique `profiles/deleted/<id>-<archive-id>/` folder and disappear from the profile selector. These files can be restored through **Import profile**. Deletion verifies the loaded revision and observes other instances' write leases. If there are no available profiles left, the app creates an empty playthrough. Game saves remain read-only.

| State | Source and behavior |
|---|---|
| All 17 apartments and 16 building categories | `Globals.tnmt`, `TenementController.general/apartments`, actual category enum values |
| Tenants and furniture-only occupancy | Saved resident asset path, with NPC ID fallback; Shop/player exclusions preserved |
| Examination and clearing | Saved flags/counts; same float32 percentage formula as the game, including zero initial counts |
| Renovation permission | Resident restrictions; `Actor[resident].CanUpgradeApartment` from saved dialogue tables |
| Active renovations | Saved category progress objects, canonical upgrade asset path, contractor, seconds remaining, readiness for final payment |
| Difficulty | Saved preset index; custom material/money/speed enum values when Custom is active; separate debug cost bypass |
| Rental revenue facts | Separate rental-income multiplier, payment schedule, current upgrade asset references, and move-in status |
| Landlord permission | Saved `TenementBlueprint31` task item, including valid amount zero |
| Material stock | User-selected carried/equipped inventory, bag contents, and contractor stock snapshots |

An active renovation's materials have already been consumed, so import never deducts them again or puts them back in the shopping list. Ready-for-payment work remains unfinished until you record completion after paying in the game. The saved remaining time is retained as snapshot metadata; OSHelp does not run a game clock.

Current upgrade asset references preserve the same value lookup that the game uses for rental revenue. Manual facility changes invalidate the corresponding cached reference, while completion records the newly completed upgrade. Older imported profiles can recover rental facts from their original save when the recorded source hash still matches; user-recorded facility changes are preserved.

Current tenants have recruitment recorded as confirmed. Unrented applicants' dialogue/quest gates still require confirmation in Tenants; this import does not execute arbitrary quest scripts or infer reachability from a single rental dialogue condition.

Future construction goals are chosen and confirmed in OSHelp. Import does not invent goals from missing facilities. Further changes are tracked in the imported profile. Reimporting the same name requires confirming replacement of the existing plan; rename the current profile first if you want to keep it as a separate playthrough snapshot.

## Stock sources

The review shows independent checkboxes for each source and counts of renovation materials/units. Only item IDs used by installed renovation bills contribute to imported stock. World loot and unrelated containers are excluded. Contractor depots are separate in the game; combining them in the shopping cart may require transferring materials to the contractor performing the work.

`TenementController.OnSavingGame` calls `RefreshResourceStorageSnapshot` before serialization. `GetItemAmount` uses the relevant contractor's snapshot when its scene is not active, plus carried inventory. This establishes why the saved per-contractor snapshots are valid sources, and why each depot is imported once rather than also scanning its scene containers.

Older saves without contractor snapshots still import carried/bag stock. Their review only offers sources actually present. Fractional saved quantities are combined before flooring to usable whole units. Duplicate references to the same inventory stack are counted once.

## Schema and validation

Files are UTF-8 JSON, sometimes with a BOM. `Globals.tnmt` holds `{ GUID, Data }` records. Newtonsoft `$id`/`$ref` links can cross record boundaries and point forward; the importer indexes the complete document before resolving them. It treats `$type` as data and never constructs game types. Upgrade ScriptableObjects reload by `addressableAssetPath` in the game; imported jobs use the installed catalog's corresponding definitions.

The save's Info and Globals timestamps can differ by fractions of a millisecond because they are written separately. The Globals timestamp identifies the actual imported state. Character/slot identity must match. File size and modification checks detect changes during reading; incomplete/unsupported data is rejected before any profile is saved.

Two game schema details need special handling:

- `TenementGeneral.currententFence` and `currententGreenhouse` are actually typed as `TenementElevator`, despite the different enum types in their upgrade target definitions. Their saved `None` is therefore decoded with the state field's actual enum.
- Older 0.4 saves use elevator `Off`. The importer maps this to zero only through the saved current-upgrade asset identifying tier zero, then shares that known alias with the other fields of the same enum. Unknown values are rejected rather than guessed.

`Globals.dialogue` contains serialized Lua data tables. A bounded parser reads scalar/table values without executing Lua. Missing actor permission fields have the game's false boolean semantics. Full recruitment quest execution remains outside this adapter.

Profile provenance includes character, slot, game version, saved/imported timestamps, directory, selected stock sources, and SHA-256 hashes of the files read. Added fields are optional in schema-1 profiles, keeping existing profiles readable.

## Verification

All 24 discovered slots across both local characters, spanning 0.4.04–0.5.19, were successfully decoded. The regression suite additionally compares all apartment facility values and carried/contractor stock with 20 stable manual slots, checks imported profile round trips, and tests synthetic reference, corruption, difficulty, clearing, payment-state, and stock-source cases. The desktop workflow exercises the real picker/review controls and reloads the imported profile from disk.

Overwrite tests cover case/whitespace matching, explicit approval, retained identity and backups, existing duplicate consolidation, stale confirmations, concurrent writers, and rollback after failed replacement. GUI checks exercise both Overwrite and Cancel and verify the resulting profile count and backup contents.

The source inspection is reproducible with `tools/Inspect-SaveRules.ps1`; selected [game IL and field evidence](evidence/save-import-rules.il.txt) is retained alongside [verification results](evidence/prototype-verification.txt). No original game save is copied into repository documentation.
