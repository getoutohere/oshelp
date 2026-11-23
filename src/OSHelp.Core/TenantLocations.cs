using System.Text.Json;

namespace OSHelp.Core;

public sealed class TenantPlacement
{
    public int NpcId { get; set; }
    public string Scene { get; set; } = "";
    public string ScenePath { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public long PathId { get; set; }
    public string Class { get; set; } = "";
    public string Hierarchy { get; set; } = "";
    public string CharacterSourceFile { get; set; } = "";
    public bool TestPlacement => Hierarchy.Split(" -> ").Any(name => name == "Test" || name.StartsWith("Test ", StringComparison.Ordinal));
}

public static class TenantLocations
{
    public static async Task EnrichAsync(string repository, string gameRoot, string dataDirectory, Catalog catalog, IProgress<string>? progress, CancellationToken cancel)
    {
        var validation = catalog.FileValidation ??= new GameFileValidation(gameRoot, dataDirectory);
        var source = File.Exists(Path.Combine(dataDirectory, "tenant-location-inputs.json")) && File.Exists(Path.Combine(dataDirectory, "tenant-locations.json")) ? dataDirectory : Path.Combine(repository, "docs/evidence");
        var inputs = Json.Read<List<InputHash>>(Path.Combine(source, "tenant-location-inputs.json"));
        bool matches = true;
        foreach (var input in inputs)
        {
            cancel.ThrowIfCancellationRequested(); progress?.Report("Checking tenant locations: " + input.File);
            var hash = await validation.HashAsync(input.File, cancel);
            if (hash != input.Hash) { matches = false; break; }
        }
        if (!matches)
        {
            // This evidence is independent of the renovation/profile fingerprint: adding
            // location metadata must not discard an existing playthrough's shopping cart.
            var staging = Path.Combine(dataDirectory, "locations-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            try
            {
                progress?.Report("Tracing tenant NPCs through game scene placements...");
                await CatalogImport.RunScript(repository, "Export-TenantLocations.ps1", gameRoot, staging, cancel);
                Apply(catalog, Json.Read<List<TenantPlacement>>(Path.Combine(staging, "tenant-locations.json")));
                foreach (var name in new[] { "tenant-locations.json", "tenant-location-inputs.json" }) AtomicFile.Write(Path.Combine(dataDirectory, name), File.ReadAllText(Path.Combine(staging, name)));
            }
            finally { if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(staging, true); }
        }
        else Apply(catalog, Json.Read<List<TenantPlacement>>(Path.Combine(source, "tenant-locations.json")));
    }
    public static void Apply(Catalog catalog, IReadOnlyList<TenantPlacement> placements)
    {
        foreach (var tenant in catalog.Tenants)
        {
            tenant.Placements = placements.Where(p => p.NpcId == tenant.NpcId).ToList();
            if (tenant.Recruitment.Count == 0)
            {
                tenant.LocationHint = tenant.InitialApartment is int apartment ? $"Your tenement · initially apt. {apartment}" : "Special resident · no rental offer";
                continue;
            }
            var locations = tenant.Placements.Where(p => !p.TestPlacement && p.Scene != "Interior Player Tenement").Select(p => SceneLabel(p.Scene)).Distinct().Order().ToArray();
            if (locations.Length == 0) throw new InvalidDataException("No recruitment location found for " + tenant.Name);
            tenant.LocationHint = string.Join(" / ", locations);
        }
    }
    public static string SceneLabel(string scene) => scene switch
    {
        "Open Sewer Tenement" => "Tenement district · outdoors",
        "Open Sewer Bazaar" => "Bazaar · outdoors",
        "Interior Player Tenement" => "Your tenement",
        "Interior Tenement Bus" => "Tenement district · bus",
        _ when scene.StartsWith("Interior Tenement ", StringComparison.Ordinal) => scene[18..],
        _ when scene.StartsWith("Interior Bazaar ", StringComparison.Ordinal) => "Bazaar · " + scene[16..],
        _ when scene.StartsWith("Interior Mines ", StringComparison.Ordinal) => "Mines · " + scene[15..],
        _ => scene
    };
}

public static class TenantRentalStatus
{
    public static IReadOnlyList<int> Apartments(Catalog catalog, Profile profile, Tenant tenant) => profile.Apartments
        .Where(a => a.Number != 1 && catalog.ApartmentType(a.Number).HasRentalUi && a.Occupant == tenant.Id && !a.OnlyFurniture)
        .Select(a => a.Number).Order().ToArray();
    public static string Label(Catalog catalog, Profile profile, Tenant tenant)
    {
        var apartments = Apartments(catalog, profile, tenant);
        if (apartments.Count > 0) return "Already renting · Apt " + string.Join(", ", apartments.Select(n => n.ToString("00")));
        return profile.Apartments.Any(a => a.Number != 1 && catalog.ApartmentType(a.Number).HasRentalUi && a.Occupant == null) ? "Not recorded" : "Not renting";
    }
}
