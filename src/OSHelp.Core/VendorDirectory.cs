namespace OSHelp.Core;

public sealed class VendorListing
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Operator { get; init; } = "";
    public string Location { get; init; } = "";
    public string Kind { get; init; } = "";
    public IReadOnlyList<Vendor> Sources { get; init; } = [];
    public IReadOnlyList<Vendor> ActiveSources { get; init; } = [];
    public VendorHours? OpeningHours { get; init; }
    public bool Conditional { get; init; }
    public bool Inactive { get; init; }
    public string Status(int day, int minute) => Inactive ? "Unavailable" : OpeningHours is not { } hours ? "Check requirements" : !hours.Contains(day, minute) ? "Closed" : Conditional ? "Open (conditional)" : "Open";
    public bool InRegion(string region) => ActiveSources.Any(v => VendorDirectory.Region(v) == region);
}

public static class VendorDirectory
{
    public static string Region(Vendor v) => v.Scene.Contains("Bazaar", StringComparison.Ordinal) ? "Bazaar" : v.Scene.Contains("Mines", StringComparison.Ordinal) ? "Mines" : v.Scene.Contains("Tenement", StringComparison.Ordinal) ? "Tenement" : "Other";
    static bool Inactive(Vendor v) => v.Notes.Any(n => n.StartsWith("Inactive", StringComparison.Ordinal));
    static string[] Operators(Vendor v) => v.Operator.Split(" / ", StringSplitOptions.RemoveEmptyEntries);
    static bool SameShop(Vendor a, Vendor b)
    {
        bool machine = a.Kind == "Vending machine", otherMachine = b.Kind == "Vending machine";
        if (machine || otherMachine) return machine && otherMachine && a.Scene == b.Scene && a.Name == b.Name;
        bool sameName = a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase);
        bool sameOperator = Operators(a).Intersect(Operators(b), StringComparer.OrdinalIgnoreCase).Any();
        if (a.Scene == b.Scene) return sameName || sameOperator;
        // Exterior display copies and inactive memories are alternate representations
        // of a staffed shop, rather than additional businesses in the directory.
        return sameName && sameOperator && Region(a) == Region(b) &&
            (a.Scene.StartsWith("Open Sewer ", StringComparison.Ordinal) || b.Scene.StartsWith("Open Sewer ", StringComparison.Ordinal) || Inactive(a) || Inactive(b));
    }
    static int Rank(Vendor v) => v.Kind switch { "Trader" or "Trader + furniture" => 0, "Market checkout" => 1, "Shop menu" => 2, "Furniture" => 3, _ => 4 };
    static string Canonical(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static IReadOnlyList<VendorListing> Build(IReadOnlyList<Vendor> vendors)
    {
        var parents = Enumerable.Range(0, vendors.Count).ToArray();
        int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        for (int i = 0; i < vendors.Count; i++) for (int j = i + 1; j < vendors.Count; j++) if (SameShop(vendors[i], vendors[j])) parents[Root(j)] = Root(i);
        var listings = new List<VendorListing>();
        foreach (var indices in Enumerable.Range(0, vendors.Count).GroupBy(Root))
        {
            var sources = indices.Select(i => vendors[i]).ToList();
            var active = sources.Where(v => !Inactive(v)).ToList(); bool inactive = active.Count == 0;
            if (inactive) active = sources;
            bool machine = sources.All(v => v.Kind == "Vending machine");
            if (!machine && active.Any(v => v.Scene.StartsWith("Interior ", StringComparison.Ordinal))) active = active.Where(v => v.Scene.StartsWith("Interior ", StringComparison.Ordinal)).ToList();
            var primary = active.OrderBy(Rank).ThenBy(v => v.Id, StringComparer.Ordinal).First();
            var hours = new List<VendorHours>(); bool missing = false;
            if (machine)
            {
                foreach (var v in active) { if (v.Accessible is { } h) hours.Add(h); else missing = true; }
            }
            else foreach (var place in active.GroupBy(v => v.Scene))
            {
                var lead = place.OrderBy(Rank).ThenBy(v => v.Id, StringComparer.Ordinal).First();
                var internalDoors = place.SelectMany(v => v.InternalAccess).DistinctBy(d => d.Source).ToList();
                if (lead.Accessible is not { } h || internalDoors.Any(d => d.Hours == null)) { missing = true; continue; }
                // A secondary furniture/checkout menu must not remove the staff's
                // timetable or a physical shop door from the shop's opening hours.
                hours.Add(VendorHours.Combine(internalDoors.Select(d => d.Hours!).Prepend(h), true));
            }
            var brands = active.Where(v => !Operators(v).Contains(v.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var sceneName = primary.Scene.Split(' ').Last();
            string name = brands.OrderByDescending(v => Canonical(v.Name) == Canonical(sceneName)).ThenByDescending(v => v.Name.Length).Select(v => v.Name).FirstOrDefault() ?? primary.Name;
            name = primary.Aliases.FirstOrDefault(a => a.EndsWith(" Stall", StringComparison.OrdinalIgnoreCase)) ?? name;
            var notes = active.SelectMany(v => v.Notes.Concat(v.Entrances.Concat(v.InternalAccess).SelectMany(e => e.Notes)));
            listings.Add(new()
            {
                Id = primary.Id, Name = name, Operator = string.Join(" / ", sources.SelectMany(Operators).Distinct().Order()),
                Location = string.Join(" / ", active.Select(v => v.Location).Distinct().Order()), Kind = machine ? "Vending machine" : "Shop",
                Sources = sources, ActiveSources = active, Inactive = inactive,
                OpeningHours = hours.Count > 0 ? VendorHours.Combine(hours, false) : null,
                Conditional = missing || notes.Any(n => n.StartsWith("Requires", StringComparison.Ordinal))
            });
        }
        return listings.OrderBy(v => v.Location).ThenBy(v => v.Name).ToList();
    }
}
