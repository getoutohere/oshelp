using System.Text.Json;
using OSHelp.Core;

internal static class RentalIncomeChecks
{
    public static void Run(Catalog catalog, string scratch, Action<bool, string> check)
    {
        var aleksi = catalog.Tenants.Single(t => t.Name == "Aleksi Kivi"); var pate = catalog.Tenants.Single(t => t.Name == "Pate Rantanen");
        var agnes = catalog.Tenants.Single(t => t.Name == "Agnes Takala"); var zoya = catalog.Tenants.Single(t => t.Name == "Zoya Gubenko");
        var profile = Profile.Create(catalog, "Income checks");
        check(profile.RentPaymentMode == 0 && catalog.RentalSettings.PresetPaymentModes["Default"] == 0 && catalog.RentalSettings.PresetPaymentModes["Casual"] == 1, "default weekly and Casual daily schedules extracted from game");
        check(RentalIncome.Payment(new(2503, 0), 0) == new RentPayment(626, 0), "reported Pate regression: 2503 monthly becomes 626 OC weekly");
        foreach (var (mode, count) in new[] { (0, 4), (1, 30), (2, 4), (3, 2), (4, 1) })
            check(RentalIncome.Payment(new(12000, 1200), mode) == new RentPayment(12000 / count, 1200m / count), "all game payment frequencies");
        check(RentalIncome.Payment(new(2506, 293), 2) == new RentPayment(626, 73.25m) && RentalIncome.Payment(new(2510, 1), 2).Oc == 628, "OC uses ties-to-even; RM preserves payment cents");
        check(RentalIncome.Payment(new(1, 1), 1) == new RentPayment(0, .03m) && RentalIncome.Payment(new(20, 20), 1) == new RentPayment(1, .67m), "daily OC and RM use independent integer and two-decimal rounding");
        foreach (var key in profile.BuildingLevels.Keys) profile.BuildingLevels[key] = 0;
        foreach (var apartment in profile.Apartments) { foreach (var key in apartment.Levels.Keys) apartment.Levels[key] = 0; apartment.Occupant = ""; }
        check(RentalIncome.ApartmentValue(catalog, profile, 4) == 5100, "small value includes 5000 base plus the initial pier asset's 100 value");
        int medium = catalog.Apartments.First(a => a.Number > 2 && catalog.ApartmentType(a.Number).Size == 1).Number;
        int large = catalog.Apartments.First(a => a.Number > 2 && catalog.ApartmentType(a.Number).Size == 2).Number;
        check(RentalIncome.ApartmentValue(catalog, profile, medium) == 7650 && RentalIncome.ApartmentValue(catalog, profile, large) == 10200, "medium and large size value factors");
        check(RentalIncome.Monthly(catalog, profile, aleksi, 4) == new MonthlyRent(3455, 0), "Aleksi actual income includes value bonus only in OC");
        check(RentalIncome.Monthly(catalog, profile, zoya, 4) == new MonthlyRent(0, 1625), "zero base OC never receives apartment OC bonus");
        check(RentalIncome.Monthly(catalog, profile, agnes, 4) == new MonthlyRent(505, 280), "both-currency resident receives both value bonuses");
        profile.Apartment(4).Occupant = pate.Id;
        var comparison = RentalIncome.Compare(catalog, profile, aleksi, 4);
        check(comparison.Occupancy.Contains(pate.Name) && comparison.Current == new MonthlyRent(1455, 0) && comparison.Selected == new MonthlyRent(3455, 0) && comparison.Change == new MonthlyRent(2000, 0), "occupied apartment replacement current/proposed/delta");
        check(comparison.CurrentPayment == new RentPayment(364, 0) && comparison.SelectedPayment == new RentPayment(864, 0) && comparison.PaymentChange == new RentPayment(500, 0) && comparison.PaymentPeriod == "week", "comparison primarily uses actual weekly payments");
        comparison = RentalIncome.Compare(catalog, profile, agnes, 4);
        check(comparison.Change == new MonthlyRent(-950, 280), "mixed currency revenue change preserves gain and loss separately");
        check(comparison.PaymentChange == new RentPayment(-238, 70), "mixed-currency weekly replacement payment");
        profile.Apartment(4).Occupant = agnes.Id;
        var rounding = RentalIncome.Compare(catalog, profile, new Tenant { Id = "rounding-fixture", IncomeOc = 1199 }, 4);
        check(rounding.Change?.Oc == 949 && rounding.PaymentChange?.Oc == 238 && RentalIncome.Payment(rounding.Change.Value, 0).Oc == 237, "replacement delta subtracts separately rounded payments, not a rounded monthly difference");
        profile.Apartment(4).Occupant = pate.Id;
        profile.RentPaymentMode = null;
        check(RentalIncome.Compare(catalog, profile, agnes, 4).SelectedPayment == null && RentalIncome.Compare(catalog, profile, agnes, 4).Selected.HasValue, "unknown payment schedule does not assume weekly");
        profile.RentPaymentMode = 0;
        check(RentalIncome.Compare(catalog, profile, pate, 4).Change == new MonthlyRent(0, 0), "same renter produces zero change");
        profile.RentalIncomeMultiplier = .75f;
        check(RentalIncome.Compare(catalog, profile, aleksi, 4).Change == new MonthlyRent(1500, 0), "rental multiplier applied to both incomes before subtracting");
        profile.RentalIncomeMultiplier = 2;
        check(RentalIncome.Monthly(catalog, profile, agnes, 4) == new MonthlyRent(1010, 560), "Casual doubles actual income rather than renovation costs");
        profile.RentalIncomeMultiplier = 1;
        profile.SetLevel("walls", 4, 1); profile.SetLevel("bathroom", 4, 1);
        check(RentalIncome.ApartmentValue(catalog, profile, 4) == 6300 && RentalIncome.Monthly(catalog, profile, aleksi, 4) == new MonthlyRent(3515, 0), "completed walls/bathroom add their actual asset values");
        profile.SetLevel("walls", 4, 0); profile.SetLevel("bathroom", 4, 0);
        var walls = catalog.Upgrades.Single(u => u.Name == "Small-Walls-1-Makeshift");
        profile.Apartment(4).CurrentUpgradeIds["walls"] = walls.Id;
        check(RentalIncome.ApartmentValue(catalog, profile, 4) == 6100, "saved cached upgrade pointer is preferred as in the game");
        profile.SetLevel("walls", 4, 0);
        check(RentalIncome.ApartmentValue(catalog, profile, 4) == 5100, "manual tier recording clears stale value pointer");
        profile.Apartment(4).MovingIn = true;
        check(RentalIncome.Compare(catalog, profile, aleksi, 4).Current == new MonthlyRent(0, 0) && RentalIncome.Compare(catalog, profile, aleksi, 4).Occupancy.Contains("moving in"), "moving-in renter earns zero until move-in complete");
        check(RentalIncome.Compare(catalog, profile, pate, 4).Change == new MonthlyRent(0, 0), "same moving-in renter is not counted as a replacement");
        profile.Apartment(4).MovingIn = false; profile.Apartment(4).OnlyFurniture = true;
        check(RentalIncome.Compare(catalog, profile, aleksi, 4).Current == new MonthlyRent(0, 0), "furniture-only placement earns no current rent");
        profile.Apartment(4).OnlyFurniture = false; profile.Apartment(4).Occupant = "";
        check(RentalIncome.Compare(catalog, profile, aleksi, 4).Change == new MonthlyRent(3455, 0), "vacant apartment starts from zero revenue");
        profile.Apartment(4).Occupant = null;
        check(RentalIncome.Compare(catalog, profile, aleksi, 4).Change == null, "unknown occupancy is not assumed vacant");
        profile.Apartment(4).Occupant = pate.Id; profile.SetLevel("walls", 4, null);
        check(RentalIncome.Compare(catalog, profile, aleksi, 4).Change == null, "unrecorded value-bearing facility does not fabricate total income");
        check(RentalIncome.Compare(catalog, profile, pate, 4).Change == new MonthlyRent(0, 0), "same renter has zero change even with missing valuation inputs");
        profile.RentalIncomeMultiplier = 0;
        check(RentalIncome.Monthly(catalog, profile, aleksi, 4) == new MonthlyRent(0, 0), "disabled rental income is zero even without full facility records");
        check(RentalIncome.Compare(catalog, profile, aleksi, 2).Change == null && RentalIncome.Compare(catalog, profile, aleksi, 1).Change == null, "non-rental units have no revenue comparison");
        int realValues = 0; bool checkedSourceGuard = false;
        foreach (var slot in SaveImport.Discover().Where(s => s.Name.StartsWith("Slot", StringComparison.Ordinal)))
        {
            var imported = SaveImport.Read(slot.Directory, catalog).Profile;
            check(imported.RentPaymentMode.HasValue, "real save has a determined payment schedule");
            var scheduleOnly = ProfileStore.Clone(imported); scheduleOnly.RentPaymentMode = null; scheduleOnly.RentalIncomeMultiplier = .75f; scheduleOnly.Apartment(4).MovingIn = !imported.Apartment(4).MovingIn;
            check(RentalIncome.RestoreImportedFacts(scheduleOnly, catalog) && scheduleOnly.RentPaymentMode == imported.RentPaymentMode && scheduleOnly.RentalIncomeMultiplier == .75f && scheduleOnly.Apartment(4).MovingIn != imported.Apartment(4).MovingIn, "payment schedule migration preserves manual income and occupancy changes");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(slot.Directory, "Globals.tnmt")));
            var ids = new Dictionary<string, JsonElement>();
            void Index(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object) { if (element.TryGetProperty("$id", out var id)) ids[id.GetString()!] = element; foreach (var p in element.EnumerateObject()) Index(p.Value); }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Index(item);
            }
            Index(json.RootElement);
            JsonElement Resolve(JsonElement element) { while (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var id)) element = ids[id.GetString()!]; return element; }
            var tenement = Resolve(json.RootElement.GetProperty("Data").EnumerateArray().Single(r => r.GetProperty("GUID").GetString() == "TenementController").GetProperty("Data"));
            var general = Resolve(tenement.GetProperty("general"));
            // Reference calculation reads the actual saved current-upgrade pointers, not recorded levels.
            int UpgradeValue(JsonElement owner, string field)
            {
                if (!owner.TryGetProperty(field, out var pointer)) return 0;
                var upgrade = Resolve(pointer); if (upgrade.ValueKind == JsonValueKind.Null) return 0;
                var path = upgrade.GetProperty("addressableAssetPath").GetString();
                return catalog.Upgrades.Single(u => u.Id == path).Source.Fields.Int("value");
            }
            string[] buildingFields = ["currentStairsUpgrade", "currentFacadeUpgrade", "currentCourtyardUpgrade", "currentSaunaUpgrade", "currentSaunaAndPierUpgrade", "currentElevatorUpgrade", "currentRoofUpgrade"];
            string[] apartmentFields = ["currentElectricityUpgrade", "currentWaterUpgrade", "currentHeatingUpgrade", "currentBathroomUpgrade", "currentWallUpgrade", "currentBalconyUpgrade", "currentBalcony2Upgrade", "currentKitchenUpgrade"];
            foreach (var saved in tenement.GetProperty("apartments").EnumerateArray().Select(Resolve))
            {
                int number = saved.GetProperty("apartmentNumber").GetInt32(); if (number < 3) continue;
                int value = 5000 + buildingFields.Sum(f => UpgradeValue(general, f)) + apartmentFields.Sum(f => UpgradeValue(saved, f));
                int size = catalog.ApartmentType(number).Size; value = size == 1 ? (int)(value * 1.5d) : size == 2 ? value * 2 : value;
                // Null pointers can be populated lazily by GetCurrent*. Compare fixtures where the saved value pointers are complete.
                bool complete = buildingFields.All(f => general.GetProperty(f).ValueKind != JsonValueKind.Null) && apartmentFields.All(f => saved.GetProperty(f).ValueKind != JsonValueKind.Null);
                if (complete) { check(RentalIncome.ApartmentValue(catalog, imported, number) == value, "real saved apartment valuation parity"); realValues++; }
                check(imported.Apartment(number).MovingIn == saved.GetProperty("moveStarted").GetBoolean(), "real saved move-in flag imported");
            }
            var legacy = ProfileStore.Clone(imported); legacy.RentalIncomeMultiplier = null; legacy.BuildingCurrentUpgradeIds.Clear(); foreach (var a in legacy.Apartments) a.CurrentUpgradeIds.Clear();
            check(RentalIncome.RestoreImportedFacts(legacy, catalog) && legacy.RentalIncomeMultiplier == imported.RentalIncomeMultiplier, "older imported profile recovers income settings from exact source hash");
            if (!checkedSourceGuard)
            {
                var stale = ProfileStore.Clone(imported); stale.RentalIncomeMultiplier = null;
                stale.ImportedSave!.Files = stale.ImportedSave.Files.Select(f => f.File == "Globals.tnmt" ? new InputHash(f.File, "DIFFERENT-SNAPSHOT") : f).ToList();
                var before = JsonSerializer.Serialize(stale, Json.Options);
                check(!RentalIncome.RestoreImportedFacts(stale, catalog) && JsonSerializer.Serialize(stale, Json.Options) == before, "changed source snapshot is never substituted into an older profile");
                checkedSourceGuard = true;
            }
        }
        Console.WriteLine($"Rental income: replacement/currency/multiplier/move-in checks and {realValues} saved valuations passed.");
    }
}
