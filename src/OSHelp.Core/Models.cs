using System.Text.Json;
using System.Text.Json.Serialization;

namespace OSHelp.Core;

public sealed record Category(string Key, string Name, bool Building, string Enum, string Flag, string Value,
    string State, string? Resident, string Contractor, string Group);

public static class Categories
{
    public static readonly Category[] All = [
        new("power", "Electricity", false, "ApartmentElectricity", "upgradeElectricity", "electricityType", "currentElectricity", "electricity", "", "electricity"),
        new("water", "Water", false, "ApartmentWater", "upgradeWater", "waterType", "currentWater", "waterMin", "", "water"),
        new("heating", "Heating", false, "ApartmentHeatingMethod", "upgradeHeatingMethod", "heatingMethod", "currentHeating", "heatingMin", "", "heating"),
        new("bathroom", "Bathroom", false, "BathroomType", "upgradeBathroom", "bathroomType", "currentBathroom", "bathroomMin", "", "bathroom"),
        new("walls", "Walls", false, "ApartmentWallType", "upgradeWalls", "wallType", "currentWall", "wallMin", "", "walls"),
        new("balcony", "Front balcony", false, "BalconyType", "upgradeBalcony", "balconyType", "currentBalcony", "balconyMin", "", "balcony"),
        new("balcony2", "Rear balcony / extension", false, "BalconyType", "upgradeBalcony2", "balcony2Type", "currentBalcony2", null, "", "balcony2"),
        new("kitchen", "Kitchen", false, "KitchenType", "upgradeKitchen", "kitchenType", "currentKitchen", "kitchenMin", "", "kitchen"),
        new("buildingPower", "Building electricity", true, "TenementElectricity", "upgradeTenementElectricity", "tenementElectricityType", "currentElectricity", null, "maxTenementElectricityUpgrade", "electricityUpgrades"),
        new("buildingWater", "Building water", true, "TenementWater", "upgradeTenementWater", "tenementWaterType", "currentWater", null, "maxTenementWaterUpgrade", "waterUpgrades"),
        new("buildingHeating", "Building heating", true, "TenementHeating", "upgradeHeating", "heatingType", "currentHeating", null, "maxTenementHeatingUpgrade", "heatingUpgrades"),
        new("elevator", "Elevator", true, "TenementElevator", "upgradeElevator", "elevatorType", "currentElevator", "elevator", "maxTenementElevatorUpgrade", "elevatorUpgrades"),
        new("stairs", "Stairs", true, "TenementStairs", "upgradeStairs", "stairsType", "currentStairs", "stairsMin", "maxTenementStairsUpgrade", "stairsUpgrades"),
        new("basement", "Basement", true, "TenementBasement", "upgradeBasement", "basementType", "currentBasement", "basementMin", "maxTenementBasementUpgrade", "basementUpgrades"),
        new("sauna", "Basement sauna", true, "BasementSauna", "upgradeBasementSauna", "basementSaunaType", "currentSauna", "basementSaunaMin", "maxBasementSaunaUpgrade", "saunaUpgrades"),
        new("shrooms", "Mushroom farm", true, "BasementShrooms", "upgradeBasementShrooms", "basementShroomsType", "currentShrooms", null, "maxBasementShroomsUpgrade", "shroomsUpgrades"),
        new("workshop", "Workshop", true, "BasementWorkShop", "upgradeWorkshop", "workshopType", "currentWorkshop", null, "maxBasementWorkShopUpgrade", "workshopUpgrades"),
        new("mine", "Mine", true, "BasementMine", "upgradeMine", "mineType", "currentMine", "mineMin", "maxBasementMineUpgrade", "mineUpgrades"),
        new("roof", "Roof", true, "TenementRoof", "upgradeRoof", "roofType", "currentRoof", null, "maxTenementRoofUpgrade", "roofUpgrades"),
        new("facade", "Facade", true, "OuterWalls", "upgradeOuterWalls", "outerWallType", "currentFacade", "outerWallsMin", "maxTenementFacadeUpgrade", "facadeUpgrades"),
        new("fence", "Fence", true, "Fence", "upgradeFence", "fenceType", "currententFence", null, "", "fenceUpgrades"),
        new("courtyard", "Courtyard", true, "Courtyard", "upgradeCourtyard", "courtyardType", "currentCourtyard", "courtyardMin", "maxCourtyardUpgrade", "courtyardUpgrades"),
        new("pier", "Sauna and pier", true, "SaunaAndPier", "upgradeSaunaAndPier", "saunaAndPierType", "currentSaunaAndPier", null, "maxSaunaAndPierUpgrade", "saunaAndPierUpgrades"),
        new("greenhouse", "Greenhouse", true, "Greenhouse", "upgradeGreenhouse", "greenhouseType", "currententGreenhouse", null, "", "greenhouseUpgrades")
    ];
    public static Category Get(string key) => All.Single(x => x.Key == key);
}

public sealed class SourceRecord
{
    public string SourceFile { get; set; } = "";
    public long PathId { get; set; }
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    public long ObjectOffset { get; set; }
    public JsonElement Fields { get; set; }
    public List<string> ExternalFiles { get; set; } = [];
    public string Location => $"{SourceFile}:{PathId}";
}
public sealed record Npc(int Id, string Name);
public sealed record Requirement(string Category, int Minimum);
public sealed record Effect(string Category, int Tier);
public sealed record Material(string ItemKey, int Amount);
public sealed record ItemDefinition(string Key, int Id, string Name);
public sealed record DialogueEntry(string Conversation, int EntryId, int ActorId, string Text, string Conditions, string Script, List<int> Incoming, string Source);

public sealed class Tenant
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int NpcId { get; set; }
    public List<int> Sizes { get; set; } = [];
    public List<Npc> Household { get; set; } = [];
    public List<Requirement> Requirements { get; set; } = [];
    public int IncomeOc { get; set; }
    public int IncomeRm { get; set; }
    public bool DisableEviction { get; set; }
    public bool NoUpgrade { get; set; }
    public string DialogueActor { get; set; } = "";
    public int? InitialApartment { get; set; }
    public List<DialogueEntry> Recruitment { get; set; } = [];
    public string RecruitmentContext { get; set; } = "";
    public string LocationHint { get; set; } = "";
    public List<TenantPlacement> Placements { get; set; } = [];
    public SourceRecord Source { get; set; } = new();
}
public sealed class Upgrade
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Effect> Effects { get; set; } = [];
    public List<Material> Materials { get; set; } = [];
    public List<string> Prerequisites { get; set; } = [];
    public int StartOc { get; set; }
    public int FinalRm { get; set; }
    public int Hours { get; set; }
    public SourceRecord Source { get; set; } = new();
    [JsonIgnore] public bool Building => Effects.Any(x => Categories.Get(x.Category).Building);
    [JsonIgnore] public int Tier => Effects.Select(x => x.Tier).DefaultIfEmpty().Max();
    [JsonIgnore] public string CategoryName => string.Join(", ", Effects.Select(x => Categories.Get(x.Category).Name));
}
public sealed class ApartmentType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Size { get; set; }
    public bool HasRentalUi { get; set; }
    public Dictionary<string, List<string>> Upgrades { get; set; } = [];
}
public sealed class ApartmentDefinition
{
    public int Number { get; set; }
    public int Floor { get; set; }
    public string TypeId { get; set; } = "";
    public string? InitialTenant { get; set; }
    public Dictionary<string, int> InitialLevels { get; set; } = [];
}
public sealed class Contractor
{
    public string Name { get; set; } = "";
    public Dictionary<string, int> Limits { get; set; } = [];
    public int ApartmentLimit { get; set; }
    public int ShopLimit { get; set; }
}
public sealed record DifficultyPreset(string Name, float Materials, float Money, float Speed);
public sealed record InputHash(string File, string Hash);
public sealed class Catalog
{
    [JsonIgnore] public GameFileValidation? FileValidation { get; set; }
    public string Fingerprint { get; set; } = "";
    public string GameRoot { get; set; } = "";
    public string AssemblyHash { get; set; } = "";
    public string PermissionItem { get; set; } = "";
    public List<InputHash> Inputs { get; set; } = [];
    public List<Tenant> Tenants { get; set; } = [];
    public List<Upgrade> Upgrades { get; set; } = [];
    public List<ApartmentType> Types { get; set; } = [];
    public List<ApartmentDefinition> Apartments { get; set; } = [];
    public List<Contractor> Contractors { get; set; } = [];
    public List<DifficultyPreset> Presets { get; set; } = [];
    public Dictionary<string, List<string>> BuildingUpgrades { get; set; } = [];
    public Dictionary<string, int> InitialBuildingLevels { get; set; } = [];
    public Dictionary<string, ItemDefinition> Items { get; set; } = [];
    public Dictionary<string, Dictionary<int, string>> Enums { get; set; } = [];
    public RentalSettings RentalSettings { get; set; } = new();
    public string TierName(string category, int tier) => Enums.TryGetValue("UpgradeType+" + Categories.Get(category).Enum, out var values) && values.TryGetValue(tier, out var name) ? name : tier.ToString();
    public Upgrade Upgrade(string id) => Upgrades.SingleOrDefault(x => x.Id == id) ?? throw new InvalidDataException("Upgrade no longer exists: " + id);
    public ApartmentType ApartmentType(int number) => Types.Single(x => x.Id == Apartments.Single(a => a.Number == number).TypeId);
    public bool Applicable(Upgrade upgrade, int number) => upgrade.Building
        ? BuildingUpgrades.Values.Any(x => x.Contains(upgrade.Id))
        : ApartmentType(number).Upgrades.Values.Any(x => x.Contains(upgrade.Id));
    public IEnumerable<Contractor> ContractorsFor(Upgrade upgrade, int number) => Contractors.Where(c => upgrade.Effects.All(e => e.Tier <= (Categories.Get(e.Category).Building ? c.Limits.GetValueOrDefault(e.Category) : ApartmentType(number).Size == 3 ? c.ShopLimit : c.ApartmentLimit)));
    public string ScopeName(int apartment) => apartment == 0 ? "Building" : $"Apartment {apartment}";
    public string UpgradeLabel(Upgrade upgrade) => string.Join(" / ", upgrade.Effects.Select(e => Categories.Get(e.Category).Name + " · " + TierName(e.Category, e.Tier)));
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty JSON: " + path);
    public static string String(this JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    public static int Int(this JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
    public static bool Bool(this JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True;
    public static IEnumerable<JsonElement> Array(this JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [];
}
