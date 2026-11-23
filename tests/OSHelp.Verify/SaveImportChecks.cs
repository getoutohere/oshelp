using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OSHelp.Core;

internal static class SaveImportChecks
{
    public static void Run(Catalog catalog, string scratch, Action<bool, string> check, Action<Action, string> reject)
    {
        var directory = Path.Combine(scratch, "save-fixture"); Directory.CreateDirectory(directory);
        var date = "2026-11-23T10:00:00Z";
        JsonObject Header() => new() { ["SaveName"] = "Slot1", ["CharacterName"] = "Fixture_Player", ["Date"] = date, ["ApplicationVersion"] = "0.5.19" };
        var info = Header(); var globals = Header();
        // Independent timestamps are normal for manually saved Info and Globals.
        info["Date"] = "2026-11-23T09:59:59.999Z";
        var records = new JsonArray(); globals["Data"] = records;
        void Record(string id, JsonObject data) => records.Add(new JsonObject { ["GUID"] = id, ["Data"] = data });
        var general = new JsonObject();
        foreach (var c in Categories.All.Where(c => c.Building)) general[c.State] = c.Key is "fence" or "greenhouse" ? "None" : catalog.TierName(c.Key, 0);
        var apartments = new JsonArray();
        foreach (var definition in catalog.Apartments)
        {
            var apartment = new JsonObject { ["apartmentNumber"] = definition.Number, ["examined"] = true, ["onlyFurniture"] = false, ["underConstruction"] = false,
                ["preplacedFurnitureAmountPrimary"] = 3, ["currentFurnitureAmountPrimary"] = 1, ["preplacedFurnitureAmountSecondary"] = 0, ["currentFurnitureAmountSecondary"] = 0, ["resident"] = null };
            foreach (var c in Categories.All.Where(c => !c.Building)) apartment[c.State] = catalog.TierName(c.Key, 0);
            apartments.Add(apartment);
        }
        var target = apartments.OfType<JsonObject>().Single(a => (int)a["apartmentNumber"]! == 4);
        var aleksi = catalog.Tenants.Single(t => t.Name == "Aleksi Kivi");
        target["resident"] = new JsonObject { ["addressableAssetPath"] = aleksi.Id, ["resident"] = new JsonObject { ["ID"] = aleksi.NpcId } };
        var upgrade = catalog.Upgrades.Single(u => u.Name == "Small-Walls-1-Makeshift");
        target["wallProgress"] = new JsonObject { ["generalUpgrade"] = false, ["apartmentUpgrade"] = true, ["apartmentNumber"] = 4, ["upgradeReady"] = true, ["timeSecondsLeft"] = 0,
            ["upgrade"] = new JsonObject { ["$ref"] = "wall-upgrade" }, ["contractor"] = new JsonObject { ["contractorName"] = "Rob Madsen" } };
        var storage = new JsonObject
        {
            ["RobMadsen"] = new JsonArray(new JsonObject { ["Key"] = 35010, ["Value"] = 7.5m }),
            ["SamuelJonasson"] = new JsonArray(new JsonObject { ["Key"] = 35010, ["Value"] = 2.5m })
        };
        var tenement = new JsonObject { ["general"] = general, ["apartments"] = apartments, ["resourceStorageSnapshotsByContractor"] = storage, ["ignoreUpgradePricesAndItems"] = false };
        Record("TenementController", tenement);
        // The referenced object is intentionally serialized after the reference.
        Record("UpgradeEvidence", new JsonObject { ["$id"] = "wall-upgrade", ["addressableAssetPath"] = upgrade.Id });
        var difficulty = new JsonObject { ["currentDifficultyIndex"] = 0, ["customDifficultySettings"] = new JsonObject { ["renovationResourceCosts"] = 3, ["renovationCost"] = 0, ["renovationSpeed"] = 8, ["rentIncome"] = 3, ["rentPayments"] = 3 } };
        Record("DifficultyController", difficulty);
        Record("TaskItemsManager", new JsonObject { ["taskItems"] = new JsonArray(new JsonObject { ["taskItem"] = new JsonObject { ["id"] = "TenementBlueprint31" }, ["amount"] = 0 }) });
        Record("Inventory", new JsonObject
        {
            ["_savingSlots"] = new JsonArray(new JsonObject { ["itemStack"] = new JsonObject { ["$id"] = "planks", ["itemId"] = 35010, ["itemAmount"] = 3 } }),
            ["_savingCharacterSlots"] = new JsonArray(new JsonObject { ["itemStack"] = new JsonObject { ["$ref"] = "planks" } })
        });
        var infoPath = Path.Combine(directory, "Info.tnmt"); var globalsPath = Path.Combine(directory, "Globals.tnmt"); var luaPath = Path.Combine(directory, "Globals.dialogue");
        void Write()
        {
            File.WriteAllText(infoPath, info.ToJsonString(), new UTF8Encoding(true)); File.WriteAllText(globalsPath, globals.ToJsonString(), new UTF8Encoding(true));
            File.WriteAllText(luaPath, "Variable={Test=true}; Actor={Resident_Crazy_Neighbor={Name=\"Resident/Crazy Neighbor\",CanUpgradeApartment=true}}; StatusTable=\"\"; RelationshipTable=\"\";", new UTF8Encoding(true));
        }
        Write(); var before = CatalogImport.Hash(globalsPath); var imported = SaveImport.Read(infoPath, catalog);
        var profile = imported.CreateProfile(imported.StockSources.Select(s => s.Id));
        check(before == CatalogImport.Hash(globalsPath), "save import never modifies source files");
        check(profile.Apartments.Count == 17 && profile.Apartment(4).Occupant == aleksi.Id, "saved occupancy and apartment joins");
        check(profile.Apartment(4).PrimaryCleared == 67 && profile.Apartment(4).SecondaryCleared == 100, "game float clearing formula including zero denominator");
        check(profile.HasRentalPermission == true, "blueprint presence at amount zero satisfies game's zero-amount check");
        var blueprint = (JsonObject)((JsonArray)((JsonObject)records.OfType<JsonObject>().Single(r => (string?)r["GUID"] == "TaskItemsManager")["Data"]!)["taskItems"]!)[0]!;
        blueprint["amount"] = -1; Write(); check(SaveImport.Read(directory, catalog).Profile.HasRentalPermission == true, "zero requested amount bypasses saved amount comparison entirely"); blueprint["amount"] = 0; Write();
        check(profile.Apartment(4).Levels["walls"] == 0, "ready-for-payment work does not become a completed facility");
        check(profile.Jobs.Single().ReadyForPayment && profile.Jobs.Single().Status == JobStatus.UnderConstruction && profile.Jobs.Single().Contractor == "Rob Madsen", "saved active renovation resolves forward reference");
        check(profile.Jobs.Single().Consumed.Count == 0 && Planner.Cart(catalog, profile).Count == 0, "imported renovation does not consume stock or require materials again");
        check(profile.Stock["game:35010"] == 13, "shared inventory references deduplicated and fractional depots summed before flooring");
        check(imported.CreateProfile(["inventory"]).Stock["game:35010"] == 3, "stock source selection respected");
        check(imported.CreateProfile([]).Stock.Count == 0, "import without stock is allowed");
        check(profile.ImportedSave?.Files.Count == 3 && profile.ImportedSave.SaveDate.ToUniversalTime().Hour == 10, "source hashes and globals timestamp retained");
        check(profile.Goals.Count == 0, "import does not invent desired construction goals");
        difficulty["currentDifficultyIndex"] = 1; Write();
        var casual = SaveImport.Read(directory, catalog).Profile;
        check(casual.ResourceMultiplier == .5f && casual.MoneyMultiplier == .5f && casual.SpeedMultiplier == 2, "saved Casual index uses actual preset");
        check(casual.RentalIncomeMultiplier == 2, "Casual imports separate double rental income");
        check(casual.RentPaymentMode == 1, "Casual imports daily payment schedule from actual preset");
        difficulty["currentDifficultyIndex"] = 2; Write();
        var custom = SaveImport.Read(directory, catalog).Profile;
        check(custom.ResourceMultiplier == .5f && custom.MoneyMultiplier == 1 && custom.SpeedMultiplier == 2, "saved custom difficulty enum gap mapping");
        check(custom.RentalIncomeMultiplier == .5f, "saved custom rental income is independent of construction multipliers");
        check(custom.RentPaymentMode == 3, "custom rent payment frequency imported independently");
        var crazy = catalog.Tenants.Single(t => t.DialogueActor.Length > 0);
        target["resident"] = new JsonObject { ["addressableAssetPath"] = crazy.Id, ["resident"] = new JsonObject { ["ID"] = crazy.NpcId } }; Write();
        check(SaveImport.Read(directory, catalog).Profile.Apartment(4).CanRenovate == true, "saved actor renovation permission parsed without executing Lua");
        target["resident"] = null; target["underConstruction"] = true; target.Remove("wallProgress"); Write();
        check(GameRules.Available(catalog, SaveImport.Read(directory, catalog).Profile, 4) == true, "saved construction flag preserves availability branch");
        target["currentWall"] = 999; Write(); reject(() => SaveImport.Read(directory, catalog), "unsupported facility values rejected"); target["currentWall"] = catalog.TierName("walls", 0);
        target["currentWall"] = "UnknownTier"; target["currentWallUpgrade"] = new JsonObject { ["addressableAssetPath"] = upgrade.Id }; Write(); reject(() => SaveImport.Read(directory, catalog), "unknown string tier is not guessed from a current-upgrade pointer"); target["currentWall"] = catalog.TierName("walls", 0);
        apartments.Add(target.DeepClone()); Write(); reject(() => SaveImport.Read(directory, catalog), "duplicate apartment rejected"); apartments.RemoveAt(apartments.Count - 1);
        target["resident"] = new JsonObject { ["$ref"] = "missing" }; Write(); reject(() => SaveImport.Read(directory, catalog), "missing object reference rejected"); target["resident"] = null;
        ((JsonObject)difficulty["customDifficultySettings"]!)["renovationSpeed"] = 17; Write(); reject(() => SaveImport.Read(directory, catalog), "unsupported nonfinite speed rejected");
        ((JsonObject)difficulty["customDifficultySettings"]!)["renovationSpeed"] = 8;
        globals["CharacterName"] = "Wrong_Character"; Write(); reject(() => SaveImport.Read(directory, catalog), "mismatched character rejected"); globals["CharacterName"] = "Fixture_Player";
        Write(); File.WriteAllText(luaPath, "os.execute(\"invalid\")"); reject(() => SaveImport.Read(directory, catalog), "executable dialogue content rejected");
        Write(); File.WriteAllText(globalsPath, "{"); reject(() => SaveImport.Read(directory, catalog), "truncated save rejected");

        // Read a changed save through the same adapter used by the Sync button.
        target["currentWall"] = catalog.TierName("walls", 1); target["wallProgress"] = null; target["underConstruction"] = false;
        general[Categories.Get("stairs").State] = catalog.TierName("stairs", 1);
        target["examined"] = false; globals["Date"] = "2026-11-23T11:00:00Z"; Write();
        var existing = imported.CreateProfile(["inventory"]); existing.Name = "My ongoing plan";
        existing.Goals.Add(new() { Apartment = 4, UpgradeId = upgrade.Id });
        var power = catalog.Upgrades.Single(u => u.Name == "Small-Electricity-1-On"); existing.Goals.Add(new() { Apartment = 4, UpgradeId = power.Id });
        Planner.Confirm(catalog, existing, existing.Goals.ToArray());
        var latest = SaveImport.Read(directory, catalog); var synced = ProfileSync.Merge(catalog, existing, latest);
        check(synced.Name == existing.Name && synced.Id == existing.Id && synced.Level("walls", 4) == 1 && synced.BuildingLevels["stairs"] == 1 && synced.Apartment(4).Examined == false, "sync reads changed tenement facts from serialized game data");
        check(synced.Jobs.Single(j => j.UpgradeId == upgrade.Id).Status == JobStatus.Completed && synced.Goals.All(g => g.UpgradeId != upgrade.Id) && synced.Goals.Any(g => g.UpgradeId == power.Id), "serialized completed construction reconciles history and retains future shopping goals");
        check(synced.Stock["game:35010"] == 3 && synced.ImportedSave!.StockSourceIds!.SequenceEqual(new[] { "inventory" }), "serialized sync preserves original selected material sources");

        int realSaves = 0;
        foreach (var slot in SaveImport.Discover())
        {
            // Manual slots are stable while the player continues playing; autosaves are independently exercised by --inspect-saves.
            if (!slot.Name.StartsWith("Slot", StringComparison.Ordinal)) continue;
            var save = SaveImport.Read(slot.Directory, catalog); var importedProfile = save.CreateProfile(save.StockSources.Select(s => s.Id));
            check(importedProfile.Apartments.Count == catalog.Apartments.Count, "real save apartment count: " + slot.Character + "/" + slot.Name);
            check(importedProfile.Apartments.All(a => a.Levels.Values.All(v => v.HasValue)), "all real save apartment levels imported");
            check(importedProfile.BuildingLevels.Values.All(v => v.HasValue), "all real save building levels imported");
            check(importedProfile.Jobs.All(j => j.Status == JobStatus.UnderConstruction) && Planner.Cart(catalog, importedProfile).Count == 0, "real saved work is already paid for in materials");
            using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(slot.Directory, "Globals.tnmt")));
            var tenementSource = source.RootElement.GetProperty("Data").EnumerateArray().Single(r => r.GetProperty("GUID").GetString() == "TenementController").GetProperty("Data");
            var inventorySource = source.RootElement.GetProperty("Data").EnumerateArray().Single(r => r.GetProperty("GUID").GetString() == "Inventory").GetProperty("Data");
            long expectedPlanks = inventorySource.GetProperty("_savingSlots").EnumerateArray().Concat(inventorySource.GetProperty("_savingCharacterSlots").EnumerateArray())
                .Select(s => s.GetProperty("itemStack")).Where(s => s.TryGetProperty("itemId", out var id) && id.GetInt32() == 35010).Sum(s => (long)s.GetProperty("itemAmount").GetInt32());
            check(save.StockSources.Single(s => s.Id == "inventory").Items.GetValueOrDefault("game:35010") == expectedPlanks, "real carried stock matches source slots");
            if (tenementSource.TryGetProperty("resourceStorageSnapshotsByContractor", out var snapshots))
            foreach (var depot in snapshots.EnumerateObject())
            {
                decimal expected = depot.Value.EnumerateArray().Where(i => i.GetProperty("Key").GetInt32() == 35010).Sum(i => i.GetProperty("Value").GetDecimal());
                check(save.StockSources.Single(s => s.Id == "contractor:" + depot.Name).Items.GetValueOrDefault("game:35010") == expected, "real contractor stock matches saved depot");
            }
            foreach (var apartment in tenementSource.GetProperty("apartments").EnumerateArray())
            {
                int number = apartment.GetProperty("apartmentNumber").GetInt32();
                foreach (var category in Categories.All.Where(c => !c.Building))
                    check(catalog.TierName(category.Key, importedProfile.Level(category.Key, number)!.Value) == apartment.GetProperty(category.State).GetString(), "real save apartment enum parity");
            }
            check(importedProfile.ImportedSave!.StockSources.Count == save.StockSources.Count, "real source choices persisted");
            var storePath = Path.Combine(scratch, "import-roundtrip"); using var store = new ProfileStore(storePath); store.Save(importedProfile);
            var loaded = store.Load(importedProfile.Id);
            check(loaded.ImportedSave!.Files.SequenceEqual(importedProfile.ImportedSave.Files) && loaded.Stock.SequenceEqual(importedProfile.Stock), "import persistence round trip");
            realSaves++;
        }
        Console.WriteLine($"Save import: synthetic corruption/reference/lifecycle checks and {realSaves} real manual slots passed.");
    }
}
