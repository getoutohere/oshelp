using System.Text.Json;
using System.Security.Cryptography;

namespace OSHelp.Core;

public sealed class RentalSettings
{
    public int FormatVersion { get; set; }
    public int BaseValue { get; set; } = 5000;
    public int OcDivider { get; set; } = 20;
    public int RmDivider { get; set; } = 200;
    public Dictionary<string, int> PresetIncomeEnums { get; set; } = [];
    public Dictionary<string, int> PresetPaymentModes { get; set; } = [];
    public Dictionary<int, string> PaymentModeNames { get; set; } = [];
    public List<InputHash> Inputs { get; set; } = [];
    public float PresetMultiplier(string name) => GameRules.Multiplier(PresetIncomeEnums.TryGetValue(name, out var value) ? value : throw new InvalidDataException("Missing rental income preset " + name));
}
public readonly record struct MonthlyRent(int Oc, int Rm)
{
    public static MonthlyRent operator -(MonthlyRent left, MonthlyRent right) => new(checked(left.Oc - right.Oc), checked(left.Rm - right.Rm));
}
public readonly record struct RentPayment(int Oc, decimal Rm)
{
    public static RentPayment operator -(RentPayment left, RentPayment right) => new(checked(left.Oc - right.Oc), left.Rm - right.Rm);
}
public sealed record RentalComparison(string Occupancy, Tenant? CurrentTenant, MonthlyRent? Current, MonthlyRent? Selected, MonthlyRent? Change, string Note)
{
    public RentPayment? CurrentPayment { get; init; }
    public RentPayment? SelectedPayment { get; init; }
    public RentPayment? PaymentChange { get; init; }
    public string PaymentPeriod { get; init; } = "payment period not recorded";
}

public static class RentalIncome
{
    public static readonly string[] ValueBuildingCategories = ["stairs", "facade", "courtyard", "sauna", "pier", "elevator", "roof"];
    public static async Task EnrichAsync(string repository, string gameRoot, string dataDirectory, Catalog catalog, IProgress<string>? progress, CancellationToken cancel)
    {
        var validation = catalog.FileValidation ??= new GameFileValidation(gameRoot, dataDirectory);
        var path = Path.Combine(dataDirectory, "rental-settings.json");
        var source = File.Exists(path) ? path : Path.Combine(repository, "docs/evidence/rental-settings.json");
        var settings = Json.Read<RentalSettings>(source); bool matches = true;
        foreach (var input in settings.Inputs)
        {
            var hash = await validation.HashAsync(input.File, cancel);
            if (hash != input.Hash) { matches = false; break; }
        }
        if (!matches || settings.FormatVersion < 2)
        {
            progress?.Report("Reading rental income difficulty settings...");
            var staging = Path.Combine(dataDirectory, "rent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            try
            {
                await CatalogImport.RunScript(repository, "Export-RentalSettings.ps1", gameRoot, staging, cancel);
                settings = Json.Read<RentalSettings>(Path.Combine(staging, "rental-settings.json"));
                AtomicFile.Write(path, JsonSerializer.Serialize(settings, Json.Options));
            }
            finally { if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(staging, true); }
        }
        catalog.RentalSettings = settings;
    }
    public static float SavedMultiplier(JsonElement difficulty, Catalog catalog)
    {
        int index = difficulty.GetProperty("currentDifficultyIndex").GetInt32();
        if (index < 0 || index >= catalog.Presets.Count) throw new InvalidDataException("Unknown saved difficulty index");
        var name = catalog.Presets[index].Name;
        if (name != "Custom") return catalog.RentalSettings.PresetMultiplier(name);
        var value = difficulty.GetProperty("customDifficultySettings").GetProperty("rentIncome");
        if (value.ValueKind == JsonValueKind.Number) return GameRules.Multiplier(value.GetInt32());
        var match = catalog.Enums["DefaultMultiplier"].Where(p => p.Value == value.GetString()).ToArray();
        if (match.Length != 1) throw new InvalidDataException("Unknown saved rental income multiplier " + value);
        return GameRules.Multiplier(match[0].Key);
    }
    public static int SavedPaymentMode(JsonElement difficulty, Catalog catalog)
    {
        int index = difficulty.GetProperty("currentDifficultyIndex").GetInt32();
        if (index < 0 || index >= catalog.Presets.Count) throw new InvalidDataException("Unknown saved difficulty index");
        var name = catalog.Presets[index].Name;
        if (name != "Custom") return catalog.RentalSettings.PresetPaymentModes[name];
        var value = difficulty.GetProperty("customDifficultySettings").GetProperty("rentPayments");
        if (value.ValueKind == JsonValueKind.Number) return value.GetInt32();
        var mode = catalog.RentalSettings.PaymentModeNames.SingleOrDefault(m => m.Value == value.GetString());
        return mode.Value != null ? mode.Key : throw new InvalidDataException("Unknown saved payment schedule " + value);
    }
    public static int PaymentsPerMonth(int mode) => mode switch { 0 or 2 => 4, 1 => 30, 3 => 2, 4 => 1, _ => throw new InvalidDataException("Unsupported rent payment schedule") };
    public static string PaymentPeriod(int mode) => mode switch { 0 or 2 => "week", 1 => "day", 3 => "fortnight", 4 => "month", _ => throw new InvalidDataException("Unsupported rent payment schedule") };
    public static RentPayment Payment(MonthlyRent monthly, int mode)
    {
        float count = PaymentsPerMonth(mode);
        int oc = checked((int)Math.Round((double)((float)monthly.Oc / count), MidpointRounding.ToEven));
        float rm = (float)Math.Round((double)((float)monthly.Rm / count), 2, MidpointRounding.ToEven);
        return new(oc, (decimal)rm);
    }
    public static string UpgradeField(Category category) => category.State.Replace("currentent", "current", StringComparison.Ordinal) + "Upgrade";
    public static int? ApartmentValue(Catalog catalog, Profile profile, int apartment)
    {
        int value = catalog.RentalSettings.BaseValue;
        foreach (var key in ValueBuildingCategories.Concat(Categories.All.Where(c => !c.Building).Select(c => c.Key)))
        {
            var category = Categories.Get(key); var ids = category.Building ? profile.BuildingCurrentUpgradeIds : profile.Apartment(apartment).CurrentUpgradeIds;
            if (ids.TryGetValue(key, out var id)) { value = checked(value + catalog.Upgrade(id).Source.Fields.Int("value")); continue; }
            var group = category.Building ? catalog.BuildingUpgrades.GetValueOrDefault(key, []) : catalog.ApartmentType(apartment).Upgrades.GetValueOrDefault(key, []);
            if (group.Count == 0) continue;
            var level = profile.Level(key, apartment); if (!level.HasValue) return null;
            // GetCurrent* returns the first matching target enum in the actual upgrade group, or null.
            var upgrade = group.Select(catalog.Upgrade).FirstOrDefault(u => u.Source.Fields.Int(category.Value) == level.Value);
            if (upgrade != null) value = checked(value + upgrade.Source.Fields.Int("value"));
        }
        return catalog.ApartmentType(apartment).Size switch { 1 => checked((int)(value * 1.5d)), 2 => checked(value * 2), _ => value };
    }
    public static MonthlyRent? Monthly(Catalog catalog, Profile profile, Tenant tenant, int apartment)
    {
        if ((tenant.IncomeOc == 0 && tenant.IncomeRm == 0) || profile.RentalIncomeMultiplier == 0) return new(0, 0);
        if (!profile.RentalIncomeMultiplier.HasValue) return null;
        var value = ApartmentValue(catalog, profile, apartment); if (!value.HasValue) return null;
        int Currency(int basis, int divisor)
        {
            if (basis == 0) return 0;
            int raw = checked((int)((float)basis + (float)value.Value / (float)divisor));
            return checked((int)((float)raw * profile.RentalIncomeMultiplier.Value));
        }
        return new(Currency(tenant.IncomeOc, catalog.RentalSettings.OcDivider), Currency(tenant.IncomeRm, catalog.RentalSettings.RmDivider));
    }
    public static RentalComparison Compare(Catalog catalog, Profile profile, Tenant selected, int apartment)
    {
        var state = profile.Apartment(apartment); var type = catalog.ApartmentType(apartment);
        if (apartment == 1 || !type.HasRentalUi) return new($"Apartment {apartment:00}: {(apartment == 1 ? "player apartment" : "Shop")} · cannot be rented", null, null, null, null, "No rental revenue comparison for this unit.");
        var current = catalog.Tenants.FirstOrDefault(t => t.Id == state.Occupant);
        string occupancy = ApartmentStatus(catalog, profile, apartment);
        MonthlyRent? before = state.OnlyFurniture || state.Occupant == "" || (current != null && state.MovingIn) ? new(0, 0) : current == null ? null : Monthly(catalog, profile, current, apartment);
        bool sameRenter = current?.Id == selected.Id && !state.OnlyFurniture;
        var after = sameRenter ? before : Monthly(catalog, profile, selected, apartment);
        var delta = sameRenter ? new MonthlyRent(0, 0) : before.HasValue && after.HasValue ? after.Value - before.Value : (MonthlyRent?)null;
        RentPayment? Paid(MonthlyRent? total) => total.HasValue && profile.RentPaymentMode.HasValue ? Payment(total.Value, profile.RentPaymentMode.Value) : null;
        var beforePayment = Paid(before); var afterPayment = Paid(after);
        var paymentChange = sameRenter ? new RentPayment(0, 0) : beforePayment.HasValue && afterPayment.HasValue ? afterPayment.Value - beforePayment.Value : (RentPayment?)null;
        string note = "Gross rent per payment at current facilities; replacement assumes move-in complete. Eligibility is checked below.";
        if (sameRenter) note = "The selected resident is already this apartment's renter; revenue is unchanged.";
        else if (!profile.RentalIncomeMultiplier.HasValue) note = "Record the rental income multiplier in State, or reimport the save.";
        else if (!after.HasValue || (current != null && !before.HasValue)) note = "Record current apartment/building facilities to calculate full rental revenue.";
        else if (!before.HasValue) note = "Record current occupancy to calculate the revenue change.";
        else if (!profile.RentPaymentMode.HasValue) note = "Record the rent payment schedule in State, or reimport the save.";
        return new(occupancy, current, before, after, delta, note) { CurrentPayment = beforePayment, SelectedPayment = afterPayment, PaymentChange = paymentChange, PaymentPeriod = profile.RentPaymentMode.HasValue ? PaymentPeriod(profile.RentPaymentMode.Value) : "payment period not recorded" };
    }
    public static string ApartmentStatus(Catalog catalog, Profile profile, int apartment)
    {
        var state = profile.Apartment(apartment); var current = catalog.Tenants.FirstOrDefault(t => t.Id == state.Occupant);
        if (apartment == 1 || !catalog.ApartmentType(apartment).HasRentalUi) return $"Apartment {apartment:00}: {(apartment == 1 ? "player apartment" : "Shop")} · cannot be rented";
        return state.OnlyFurniture ? $"Apartment {apartment:00}: vacant (furniture only)" : state.Occupant == null ? $"Apartment {apartment:00}: occupancy not recorded" : current != null ? $"Apartment {apartment:00}: rented to {current.Name}{(state.MovingIn ? " (moving in)" : "")}" : state.Occupant == "" ? $"Apartment {apartment:00}: vacant" : $"Apartment {apartment:00}: renter not in catalog";
    }
    public static bool RestoreImportedFacts(Profile profile, Catalog catalog)
    {
        if ((profile.RentalIncomeMultiplier.HasValue && profile.RentPaymentMode.HasValue) || profile.ImportedSave == null) return false;
        var expected = profile.ImportedSave.Files.FirstOrDefault(f => f.File == "Globals.tnmt")?.Hash;
        var path = Path.Combine(profile.ImportedSave.Directory, "Globals.tnmt");
        if (expected == null || !File.Exists(path)) return false;
        try
        {
            var restored = ProfileStore.Clone(profile);
            var bytes = File.ReadAllBytes(path); if (Convert.ToHexString(SHA256.HashData(bytes)) != expected) return false;
            using var json = JsonDocument.Parse(bytes.AsMemory(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0), new JsonDocumentOptions { MaxDepth = 256 });
            var refs = new SaveImport.References(json.RootElement);
            var records = json.RootElement.GetProperty("Data").EnumerateArray().ToDictionary(r => r.String("GUID"), r => refs.Resolve(r.GetProperty("Data")));
            float multiplier = SavedMultiplier(records["DifficultyController"], catalog);
            int paymentMode = SavedPaymentMode(records["DifficultyController"], catalog);
            if (profile.RentalIncomeMultiplier.HasValue) { profile.RentPaymentMode = paymentMode; return true; }
            var general = refs.Resolve(records["TenementController"].GetProperty("general"));
            void RestoreIds(JsonElement owner, Dictionary<string, string> target, bool building, int apartment)
            {
                foreach (var c in Categories.All.Where(c => c.Building == building))
                {
                    if (!owner.TryGetProperty(c.State, out var level) || !owner.TryGetProperty(UpgradeField(c), out var pointer)) continue;
                    // Preserve facilities that the user has changed since this imported snapshot.
                    var recorded = profile.Level(c.Key, apartment);
                    if (!recorded.HasValue || (level.ValueKind == JsonValueKind.String ? level.GetString() != catalog.TierName(c.Key, recorded.Value) : level.GetInt32() != recorded.Value)) continue;
                    var upgrade = refs.Resolve(pointer); var id = upgrade.ValueKind == JsonValueKind.Object ? upgrade.String("addressableAssetPath") : "";
                    if (id.Length > 0 && !target.ContainsKey(c.Key)) target[c.Key] = catalog.Upgrade(id).Id;
                }
            }
            RestoreIds(general, restored.BuildingCurrentUpgradeIds, true, 0);
            foreach (var saved in records["TenementController"].GetProperty("apartments").EnumerateArray().Select(refs.Resolve))
            {
                var state = restored.Apartment(saved.GetProperty("apartmentNumber").GetInt32());
                RestoreIds(saved, state.CurrentUpgradeIds, false, state.Number);
                var resident = refs.Resolve(saved.GetProperty("resident"));
                if ((resident.ValueKind == JsonValueKind.Null && state.Occupant == "") || (resident.ValueKind == JsonValueKind.Object && resident.String("addressableAssetPath") == state.Occupant)) state.MovingIn = saved.Bool("moveStarted");
            }
            profile.RentalIncomeMultiplier = multiplier; profile.RentPaymentMode ??= paymentMode; profile.BuildingCurrentUpgradeIds = restored.BuildingCurrentUpgradeIds;
            foreach (var apartment in restored.Apartments) { profile.Apartment(apartment.Number).CurrentUpgradeIds = apartment.CurrentUpgradeIds; profile.Apartment(apartment.Number).MovingIn = apartment.MovingIn; }
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }
}
