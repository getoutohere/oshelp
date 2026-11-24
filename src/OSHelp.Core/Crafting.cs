using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace OSHelp.Core;

public enum CraftingSearchMode { Input, Output }
public enum CraftingSort { Recipe, Inputs, Outputs, Workstations }

public sealed record CraftingGroupMember(string Key, string Tag, string OutputTag);
public sealed class CraftingItem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public List<CraftingGroupMember> Members { get; set; } = [];
    public string ContainedLiquid { get; set; } = "";
    public bool Liquid { get; set; }
}
public sealed class CraftingLine
{
    public string Key { get; set; } = "";
    public double Amount { get; set; }
    public List<string> Alternatives { get; set; } = [];
    public string Note { get; set; } = "";
}
public sealed class CraftingRecipe
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public List<CraftingLine> Inputs { get; set; } = [];
    public List<CraftingLine> Outputs { get; set; } = [];
    public List<CraftingLine> FailureOutputs { get; set; } = [];
    public List<string> Required { get; set; } = [];
    public List<string> Excluded { get; set; } = [];
    public string Skill { get; set; } = "";
    public int MinimumSkill { get; set; }
    public bool HardMinimumSkill { get; set; }
    public bool BlueprintRequired { get; set; }
    public bool StolenOnly { get; set; }
    public bool LiquidContainers { get; set; }
    public double BaseSeconds { get; set; }
    public double BaseFailureChance { get; set; }
    public string Source { get; set; } = "";
}
public sealed class CraftingStation
{
    public string Name { get; set; } = "";
    public List<string> Types { get; set; } = [];
    public List<string> Modifiers { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
    public List<string> Locations { get; set; } = [];
    public bool Furniture { get; set; }
    public int Kind { get; set; }
    public int MaxUpgrades { get; set; }
    public double Efficiency { get; set; }
}
public sealed class CraftingAddon
{
    public string Name { get; set; } = "";
    public string Identity { get; set; } = "";
    public int Kind { get; set; }
    public List<string> RequiredTypes { get; set; } = [];
    public bool AnyType { get; set; }
    public List<string> Modifiers { get; set; } = [];
    public double Efficiency { get; set; }
    public double Range { get; set; }
    public string Source { get; set; } = "";
}
public sealed class CraftingCatalog
{
    public const int CurrentFormat = 2;
    public int FormatVersion { get; set; } = CurrentFormat;
    public string Fingerprint { get; set; } = "";
    public List<InputHash> Inputs { get; set; } = [];
    public Dictionary<string, CraftingItem> Items { get; set; } = [];
    public List<CraftingRecipe> Recipes { get; set; } = [];
    public List<CraftingStation> Stations { get; set; } = [];
    public List<CraftingAddon> Addons { get; set; } = [];
    [JsonIgnore] public GameFileValidation? FileValidation { get; set; }
    [JsonIgnore] public bool LoadedFromCache { get; set; }
}
public sealed record CraftingStationMatch(CraftingStation Station, IReadOnlyList<CraftingAddon> Addons)
{
    public string Label => Station.Name + (!Station.Furniture ? " (" + (Station.Locations.Count > 0 ? string.Join(", ", Station.Locations) : string.Join(" / ", Station.Types) + " equipment") + ")" : "")
        + (Addons.Count == 0 ? "" : " + " + string.Join(" + ", Addons.Select(a => a.Name)));
}
public sealed class CraftingEntry
{
    public required CraftingRecipe Recipe { get; init; }
    public required IReadOnlyList<CraftingStationMatch> Workstations { get; init; }
    public required string Inputs { get; init; }
    public required string Outputs { get; init; }
    public required string StationText { get; init; }
    public bool IsFood { get; init; }
    public string Name => Recipe.Name;
}
public sealed record CraftingSearchResult(int Total, IReadOnlyList<CraftingEntry> Rows);

/// <summary>Immutable, in-memory item-to-recipe indexes. Searching never accesses files.</summary>
public sealed class CraftingDirectory
{
    // The game tags fresh dough separately from edible food. Include food
    // preparation outputs without classifying unrelated uses of food inputs.
    static readonly HashSet<string> foodCategories = ["Food", "Dough"];
    readonly CraftingCatalog catalog;
    readonly CraftingEntry[] entries;
    readonly (string Key, string Text)[] itemNames;
    readonly Dictionary<string, HashSet<int>> inputs = [], outputs = [], failures = [];
    public CraftingCatalog Catalog => catalog;

    public CraftingDirectory(CraftingCatalog catalog)
    {
        this.catalog = catalog;
        entries = catalog.Recipes.Select(r =>
        {
            var workstations = MatchStations(catalog, r);
            return new CraftingEntry { Recipe = r, Workstations = workstations,
                IsFood = r.Outputs.SelectMany(l => l.Alternatives.Prepend(l.Key)).Any(k => catalog.Items[k].Categories.Any(foodCategories.Contains)),
                Inputs = string.Join("; ", r.Inputs.Select(l => Line(l, false))),
                Outputs = string.Join("; ", r.Outputs.Select(l => Line(l, false))),
                StationText = workstations.Count == 0 ? r.Type + " (no matching equipment)" : string.Join("; ", workstations.Select(s => s.Label).Distinct()) };
        }).ToArray();
        for (int index = 0; index < entries.Length; index++)
        {
            Index(inputs, entries[index].Recipe.Inputs, index);
            Index(outputs, entries[index].Recipe.Outputs, index);
            Index(failures, entries[index].Recipe.FailureOutputs, index);
        }
        itemNames = catalog.Items.Values.Select(i => (i.Key, Normalize(i.Name + " " + i.Key))).ToArray();
    }
    static void Index(Dictionary<string, HashSet<int>> index, IEnumerable<CraftingLine> lines, int row)
    {
        foreach (var key in lines.SelectMany(l => l.Alternatives.Prepend(l.Key)).Distinct())
        {
            if (!index.TryGetValue(key, out var matches)) index[key] = matches = [];
            matches.Add(row);
        }
    }
    public static string Normalize(string query) => new(query.Trim().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).Select(char.ToUpperInvariant).ToArray());

    public CraftingSearchResult Search(string query, CraftingSearchMode mode, bool includeFailures = false,
        int limit = 100, CraftingSort sort = CraftingSort.Recipe, bool descending = false, CancellationToken cancel = default,
        bool includeFood = true, bool includeNonFood = true)
    {
        string term = Normalize(query);
        if (term.Length == 0 || !includeFood && !includeNonFood) return new(0, []);
        var matches = new HashSet<int>(); var index = mode == CraftingSearchMode.Input ? inputs : outputs;
        foreach (var item in itemNames)
        {
            cancel.ThrowIfCancellationRequested();
            if (!item.Text.Contains(term, StringComparison.Ordinal)) continue;
            if (index.TryGetValue(item.Key, out var rows)) matches.UnionWith(rows);
            if (mode == CraftingSearchMode.Output && includeFailures && failures.TryGetValue(item.Key, out rows)) matches.UnionWith(rows);
            // A filled bottle can supply a recipe's liquid without consuming the bottle itself.
            if (mode == CraftingSearchMode.Input && catalog.Items[item.Key].ContainedLiquid is { Length: > 0 } liquid)
                foreach (int row in inputs.GetValueOrDefault(liquid) ?? [])
                    if (entries[row].Recipe.LiquidContainers) matches.Add(row);
        }
        string Value(CraftingEntry e) => sort switch { CraftingSort.Inputs => e.Inputs, CraftingSort.Outputs => e.Outputs, CraftingSort.Workstations => e.StationText, _ => e.Name };
        var found = matches.Select(i => entries[i]).Where(e => e.IsFood ? includeFood : includeNonFood).ToArray();
        var ordered = descending ? found.OrderByDescending(Value, StringComparer.CurrentCultureIgnoreCase) : found.OrderBy(Value, StringComparer.CurrentCultureIgnoreCase);
        return new(found.Length, ordered.ThenBy(e => e.Recipe.Key, StringComparer.Ordinal).Take(Math.Max(1, limit)).ToArray());
    }
    public string Line(CraftingLine line, bool detail)
    {
        var item = catalog.Items[line.Key];
        var amount = line.Amount.ToString("0.########", CultureInfo.CurrentCulture);
        var text = amount + (item.Liquid ? " L " : " × ") + item.Name;
        if (detail && line.Alternatives.Count > 0)
            text += "\r\n    Alternatives: " + string.Join("; ", line.Alternatives.Select(k => catalog.Items[k].Name).Distinct());
        if (detail && line.Note.Length > 0) text += "\r\n    " + line.Note;
        return text;
    }
    public static IReadOnlyList<CraftingStationMatch> MatchStations(CraftingCatalog data, CraftingRecipe recipe)
    {
        var result = new List<CraftingStationMatch>();
        foreach (var station in data.Stations.Where(s => s.Types.Contains(recipe.Type)))
        {
            if (recipe.Excluded.Intersect(station.Modifiers).Any()) continue;
            var missing = recipe.Required.Except(station.Modifiers).ToHashSet();
            if (missing.Count == 0) { result.Add(new(station, [])); continue; }
            var available = data.Addons.Where(a => a.Kind == station.Kind
                && (a.AnyType ? a.RequiredTypes.Intersect(station.Types).Any() : a.RequiredTypes.All(station.Types.Contains))
                && !a.Modifiers.Intersect(recipe.Excluded).Any() && a.Modifiers.Any(missing.Contains)).ToArray();
            // An upgrade ID can be connected only once. Only add-ons needed for
            // required flags are considered; a speed-only upgrade is not required.
            var chosen = new List<CraftingAddon>();
            bool Pick(int next)
            {
                if (missing.All(m => chosen.Any(a => a.Modifiers.Contains(m)))) return true;
                if (station.MaxUpgrades >= 0 && chosen.Count >= station.MaxUpgrades) return false;
                for (int i = next; i < available.Length; i++)
                {
                    if (chosen.Any(a => a.Identity == available[i].Identity)) continue;
                    chosen.Add(available[i]); if (Pick(i + 1)) return true; chosen.RemoveAt(chosen.Count - 1);
                }
                return false;
            }
            if (Pick(0)) result.Add(new(station, chosen.ToArray()));
        }
        return result.OrderBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
