using System.Text.Json;

namespace OSHelp.Core;

public sealed class VendorRecord
{
    public string File { get; set; } = "";
    public string Scene { get; set; } = "";
    public long Id { get; set; }
    public string Class { get; set; } = "";
    public long GameObject { get; set; }
    public bool Enabled { get; set; }
    public bool Active { get; set; }
    public string Hierarchy { get; set; } = "";
    public List<long> Ancestors { get; set; } = [];
    public JsonElement Fields { get; set; }
    public List<string> ExternalFiles { get; set; } = [];
    public string Key => File + ":" + Id;
    public string Pointer(JsonElement pointer) => (pointer.Int("fileId") == 0 ? File : Path.GetFileName(ExternalFiles[pointer.Int("fileId") - 1])) + ":" + pointer.GetProperty("pathId").GetInt64();
    public bool Test => Hierarchy.Split(" -> ").Any(s => s == "Test" || s.StartsWith("Test ", StringComparison.Ordinal));
}

public static class VendorImport
{
    public static readonly string[] VendorClasses = ["Trade", "TradeSpecial", "FurnitureShop", "TrainShop", "MarketShop", "VendingMachine"];
    static bool EntrypointHas(VendorRecord door, string word) => door.Fields.String("OtherEntrypoint")
        .Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries).Contains(word, StringComparer.OrdinalIgnoreCase);
    static string Canonical(string name) => new(name.Normalize(System.Text.NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    static IEnumerable<string> NameWords(string name) => System.Text.RegularExpressions.Regex.Matches(name, @"[\p{L}\p{Nd}]+").Select(m => Canonical(m.Value));
    sealed record Effect(string Target, string Kind, bool Open, List<string> Evidence, List<string> Notes);
    sealed record Control(string Target, string Kind, VendorHours Hours, List<string> Evidence, List<string> Notes);

    public static VendorHours OpeningTimes(JsonElement times)
    {
        var hours = new VendorHours();
        for (int d = 0; d < 7; d++)
        {
            int start = times.GetProperty("opensOn" + VendorHours.DayNames[d]).GetInt32() * 60, end = times.GetProperty("closesOn" + VendorHours.DayNames[d]).GetInt32() * 60;
            if (start == end) continue;
            if (start < end) hours.Days[d].Add(new(start, end));
            else { if (end > 0) hours.Days[d].Add(new(0, end)); if (start < 1440) hours.Days[d].Add(new(start, 1440)); }
        }
        hours.Validate(); return hours;
    }
    public static VendorCatalog Build(IReadOnlyList<VendorRecord> rows, List<InputHash> inputs, int filesScanned)
    {
        var records = rows.ToDictionary(r => r.Key); var controls = new List<Control>();
        List<Effect> Trace(VendorRecord owner, JsonElement outputs, HashSet<string> visited, List<string> chain, List<string> conditions)
        {
            var effects = new List<Effect>();
            foreach (var output in outputs.EnumerateArray())
            {
                if (output.Bool("disabled") || (output.Bool("fireOnceOnly") && output.Bool("firedOnce"))) continue;
                var notes = new List<string>(conditions);
                if (output.TryGetProperty("delay", out var delay) && delay.GetSingle() > 0) notes.Add($"Callback delayed by {delay.GetSingle()} seconds.");
                foreach (var call in output.GetProperty("OnTrigger").GetProperty("m_PersistentCalls").Array("m_Calls"))
                {
                    if (call.Int("m_CallState") == 0) continue;
                    string key = owner.Pointer(call.GetProperty("m_Target")), method = call.String("m_MethodName");
                    if (records.TryGetValue(key, out var relay) && relay.Class == "Relay" && method is "triggerOutputs" or "Trigger")
                    {
                        if (!visited.Add(key)) continue;
                        var nextNotes = new List<string>(notes);
                        if (relay.Fields.Array("requiredTaskItems").Any() || relay.Fields.Array("requiredObjectives").Any() || relay.Fields.Array("requiredDialogueVariables").Any()) nextNotes.Add("Requires relay quest/task conditions at " + key + ".");
                        effects.AddRange(Trace(relay, relay.Fields.GetProperty("outputs"), visited, [.. chain, key], nextNotes)); visited.Remove(key); continue;
                    }
                    bool? open = method switch { "UnLock" or "Unlock" or "UnlockOpen" or "UnlockOpenOpposite" => true, "Lock" or "LockAndClose" => false, "SetActive" or "set_enabled" or "ChangeState" => call.GetProperty("m_Arguments").Bool("m_BoolArgument"), "ChangeHiddenStatus" => !call.GetProperty("m_Arguments").Bool("m_BoolArgument"), _ => null };
                    string kind = method == "SetActive" ? "Object" : method == "set_enabled" ? "Component" : "Door";
                    var evidence = new List<string>(chain) { key + "." + method };
                    if (method == "ChangeState")
                    {
                        if (!records.TryGetValue(key, out var state) || state.Class != "GameObjectStateSave") continue;
                        key = state.Fields.GetProperty("currentGameObject").GetProperty("pathId").GetInt64() != 0 ? state.Pointer(state.Fields.GetProperty("currentGameObject")) : state.File + ":" + state.GameObject; kind = "Object"; evidence.Add(key + " activation");
                    }
                    if (method == "ChangeHiddenStatus")
                    {
                        if (!records.TryGetValue(key, out var fade) || fade.Class != "FadeGameObject") continue;
                        key = fade.File + ":" + fade.GameObject; kind = "Object"; evidence.Add(key + " visibility");
                    }
                    if (open.HasValue) effects.Add(new(key, kind, open.Value, evidence, notes));
                }
            }
            return effects;
        }
        List<Effect> RelayEffects(VendorRecord owner, string field)
        {
            if (!owner.Fields.TryGetProperty(field, out var p) || p.GetProperty("pathId").GetInt64() == 0) return [];
            string key = owner.Pointer(p); if (!records.TryGetValue(key, out var relay) || relay.Class != "Relay") throw new InvalidDataException("Unresolved vendor schedule relay " + key);
            var notes = new List<string>();
            if (relay.Fields.Array("requiredTaskItems").Any() || relay.Fields.Array("requiredObjectives").Any() || relay.Fields.Array("requiredDialogueVariables").Any()) notes.Add("Requires relay quest/task conditions at " + key + ".");
            return Trace(relay, relay.Fields.GetProperty("outputs"), [relay.Key], [owner.Key, relay.Key], notes);
        }
        foreach (var source in rows.Where(r => r.Enabled && r.Class is "RelayWeekdays" or "Logic_openingTimes" or "Trade" or "TradeSpecial"))
        {
            if (source.Class is "Trade" or "TradeSpecial" && !source.Fields.Bool("autoOpenClose")) continue;
            var schedule = OpeningTimes(source.Fields.GetProperty("openingTimes"));
            var open = source.Class == "Logic_openingTimes" ? Trace(source, source.Fields.GetProperty("outputsOpen"), [], [source.Key], []) : RelayEffects(source, "onOpen");
            var closed = source.Class == "Logic_openingTimes" ? Trace(source, source.Fields.GetProperty("outputsClosed"), [], [source.Key], []) : RelayEffects(source, "onClosed");
            foreach (var effect in open)
            {
                var opposite = closed.Where(c => c.Target == effect.Target && c.Kind == effect.Kind && c.Open != effect.Open).ToList(); if (opposite.Count == 0) continue;
                var notes = effect.Notes.Concat(opposite.SelectMany(e => e.Notes)).Distinct().ToList();
                if (!source.Active) notes.Add("Requires activation of schedule controller " + source.Key + ".");
                if (source.Class == "Logic_openingTimes") notes.Add("Requires the opening-hours logic to be triggered; it does not run its own clock.");
                if (source.Fields.TryGetProperty("playerDistanceOnClose", out var distance) && distance.GetSingle() > 0) notes.Add($"Closing waits until the player is {distance.GetSingle()} m away.");
                controls.Add(new(effect.Target, effect.Kind, effect.Open ? schedule : schedule.Invert(), effect.Evidence.Concat(opposite.SelectMany(e => e.Evidence)).Distinct().ToList(), notes));
            }
        }
        foreach (var source in rows.Where(r => r.Class == "DoorSchedule" && r.Enabled))
        {
            var hours = new VendorHours();
            foreach (var time in source.Fields.Array("OpenTimes")) { int d = time.Int("x"), start = time.Int("y") * 60, end = time.Int("z") * 60; if (d is < 0 or > 6) throw new InvalidDataException("Unsupported DoorSchedule weekday"); if (start < end) hours.Days[d].Add(new(start, end)); }
            // DoorSchedule uses non-wrapping intervals, unlike OpeningTimes.
            hours = VendorHours.Combine([hours], false); hours.Validate();
            foreach (var door in source.Fields.Array("Doors")) controls.Add(new(source.Pointer(door), "Door", hours, [source.Key], source.Active ? [] : ["Requires activation of schedule controller " + source.Key + "."]));
        }
        List<Control> ObjectControls(VendorRecord record) => controls.Where(c => c.Kind == "Object" && record.Ancestors.Any(a => c.Target == record.File + ":" + a) || c.Kind == "Component" && c.Target == record.Key).ToList();
        var characters = rows.Where(r => r.Class == "Character").GroupBy(r => r.Fields.Int("ID")).ToDictionary(g => g.Key, g => g.First().Fields.String("Name") + (g.First().Fields.String("LastName") is { Length: > 0 } last ? " " + last : ""));
        int NpcId(VendorRecord r) => r.Class == "info_NPCSpawn" ? r.Fields.GetProperty("_NPC").Int("ID") : records.TryGetValue(r.Pointer(r.Fields.GetProperty("character")), out var character) ? character.Fields.Int("ID") : -1;
        var npcs = rows.Where(r => r.Class is "NPCInfo" or "info_NPCSpawn" && !r.Test).ToArray();
        HashSet<string> MenuTargets(VendorRecord relay, HashSet<string> visited)
        {
            var targets = new HashSet<string>(); if (!visited.Add(relay.Key)) return targets;
            foreach (var output in relay.Fields.Array("outputs"))
            {
                if (output.Bool("disabled")) continue;
                foreach (var call in output.GetProperty("OnTrigger").GetProperty("m_PersistentCalls").Array("m_Calls"))
                {
                    if (call.Int("m_CallState") == 0) continue;
                    string target = relay.Pointer(call.GetProperty("m_Target")); if (!records.TryGetValue(target, out var record)) continue;
                    if (VendorClasses.Contains(record.Class) && call.String("m_MethodName") is "StartTrade" or "Interact" or "EnterExternally") targets.Add(target);
                    else if (record.Class is "Relay" or "RelayDialogue") targets.UnionWith(MenuTargets(record, visited));
                }
            }
            return targets;
        }
        var talksByMenu = new Dictionary<string, List<VendorRecord>>();
        foreach (var talk in rows.Where(r => r.Class == "InteractableTalk" && !r.Test))
            foreach (var pointer in talk.Fields.Array("dialogueRelays"))
                if (records.TryGetValue(talk.Pointer(pointer), out var relay))
                    foreach (var target in MenuTargets(relay, [])) { if (!talksByMenu.TryGetValue(target, out var talks)) talksByMenu[target] = talks = []; if (!talks.Contains(talk)) talks.Add(talk); }
        bool Background(VendorRecord r) => r.Hierarchy.Split(" -> ").Any(s => s.Contains("background", StringComparison.OrdinalIgnoreCase));
        var linkedFurniture = rows.Where(r => r.Class is "Trade" or "TradeSpecial" && r.Fields.TryGetProperty("furniture", out var p) && p.GetProperty("pathId").GetInt64() != 0).Select(r => r.Pointer(r.Fields.GetProperty("furniture"))).ToHashSet();
        var result = new VendorCatalog { Inputs = inputs, FilesScanned = filesScanned, ComponentCounts = rows.GroupBy(r => r.Class).ToDictionary(g => g.Key, g => g.Count()) };
        foreach (var door in rows.Where(d => d.Class is "Door" or "DoorChangelevel" && !d.Test && !Background(d)))
        {
            var gates = controls.Where(c => c.Kind == "Door" && c.Target == door.Key).ToList(); if (gates.Count == 0) continue;
            result.TimedDoors.Add(new() { Name = door.Hierarchy, Scene = door.Scene, Source = door.Key, Hours = VendorHours.Combine(gates.Select(c => c.Hours), true), Evidence = gates.SelectMany(g => g.Evidence).Distinct().ToList(), Notes = gates.SelectMany(g => g.Notes).Distinct().ToList() });
        }
        foreach (var row in rows.Where(r => VendorClasses.Contains(r.Class) && !r.Test && !linkedFurniture.Contains(r.Key)))
        {
            var f = row.Fields; string title = row.Class == "MarketShop" ? f.String("marketName") : f.String("title");
            var vendor = new Vendor { Id = row.Key, Scene = row.Scene, Location = row.Scene.StartsWith("Interior Tenement ", StringComparison.Ordinal) ? "Tenement · " + row.Scene[18..] : row.Scene == "Open Sewer Mines" ? "Mines · outdoors" : TenantLocations.SceneLabel(row.Scene), Name = title, Kind = row.Class switch { "FurnitureShop" => "Furniture", "TrainShop" => "Trains", "MarketShop" => "Market checkout", _ when f.TryGetProperty("furniture", out var furniture) && furniture.GetProperty("pathId").GetInt64() != 0 => "Trader + furniture", _ => "Trader" }, Evidence = [row.Key + " " + row.Class, row.Hierarchy] };
            int ownerId = f.TryGetProperty("owner", out var owner) ? owner.Int("ID") : f.TryGetProperty("Trader", out owner) ? owner.Int("ID") : 0;
            if (ownerId >= 10000) ownerId = 0;
            var nearby = npcs.Where(n => n.File == row.File).Select(n => (Row: n, Common: row.Ancestors.FindIndex(a => n.Ancestors.Contains(a)))).Where(n => n.Common is >= 0 and <= 2).OrderBy(n => n.Common).ToList();
            if (ownerId <= 0 && nearby.Count > 0 && row.Class != "VendingMachine") { var closest = nearby.Where(n => n.Common == nearby[0].Common).Select(n => NpcId(n.Row)).Distinct().ToArray(); if (closest.Length == 1 && closest[0] > 0) ownerId = closest[0]; }
            vendor.Operator = ownerId > 0 ? characters.GetValueOrDefault(ownerId, f.TryGetProperty("owner", out owner) ? owner.String("name") : "") : "";
            var menuTalks = talksByMenu.GetValueOrDefault(row.Key, []);
            if (menuTalks.Count > 0) vendor.Operator = string.Join(" / ", menuTalks.Select(t => t.Fields.String("title")).Where(t => t.Length > 0).Distinct());
            if (row.Class == "VendingMachine") vendor.Kind = title is "Vending Machine" or "Soda Fountain" or "Coffee Machine" ? "Vending machine" : "Shop menu";
            if (vendor.Name.Length == 0) vendor.Name = vendor.Operator.Length > 0 ? vendor.Operator : vendor.Kind == "Furniture" ? vendor.Location + " furnishings" : row.Hierarchy.Split(" -> ")[0];
            foreach (var label in rows.Where(r => r.Class == "Storage" && r.File == row.File && r.GameObject == row.GameObject))
            {
                var alias = label.Fields.String("title"); if (alias.Length == 0 || alias == vendor.Name) continue;
                // Some stalls retain another stall's copied storage title. A
                // category label is a public alias only when this trader actually
                // declares that category (e.g. Hardware, rather than Plants/Junk).
                if (alias.EndsWith(" Stall", StringComparison.OrdinalIgnoreCase) && !f.Array("allowedCategories").Any(c => string.Equals(c.GetString(), alias[..^6], StringComparison.OrdinalIgnoreCase))) continue;
                vendor.Aliases.Add(alias); vendor.Evidence.Add(label.Key + " Storage.title: " + alias + " (same GameObject)");
            }
            bool isTrade = row.Class is "Trade" or "TradeSpecial";
            vendor.Trading = isTrade && f.Bool("autoOpenClose") ? OpeningTimes(f.GetProperty("openingTimes")) : isTrade && !f.Bool("open") ? null : VendorHours.AllDay();
            if (isTrade && !f.Bool("autoOpenClose")) vendor.Notes.Add(f.Bool("open") ? "Trader has no automatic timed closure." : "Trader starts closed and has no automatic schedule; opening is controlled by game state.");
            var actorPaths = (menuTalks.Count > 0 ? menuTalks : npcs.Where(n => n.File == row.File && NpcId(n) == ownerId && ownerId > 0)).Select(ObjectControls).ToList();
            var actorControls = actorPaths.SelectMany(p => p).Distinct().ToList();
            var ownControls = ObjectControls(row); var objectControls = ownControls.Concat(actorControls).Distinct().ToList();
            if (actorPaths.Count > 0 && vendor.Trading != null) vendor.Trading = VendorHours.Combine([vendor.Trading, VendorHours.Combine(actorPaths.Select(path => VendorHours.Combine(path.Select(c => c.Hours), true)), false)], true);
            if (ownControls.Count > 0 && vendor.Trading != null) vendor.Trading = VendorHours.Combine(ownControls.Select(c => c.Hours).Prepend(vendor.Trading), true);
            foreach (var c in objectControls) { vendor.Evidence.AddRange(c.Evidence); vendor.Notes.AddRange(c.Notes); }
            if ((!row.Active && objectControls.Count == 0) || !row.Enabled) vendor.Notes.Add("Inactive in the scene's starting state; activation depends on game state.");
            if (menuTalks.Count > 0) { vendor.Notes.Add("Open this shop through the operator's dialogue."); vendor.Evidence.AddRange(menuTalks.Select(t => t.Key + " dialogue menu")); }
            else if (f.Bool("interactDisabled") || isTrade && !f.Bool("isInteractable")) vendor.Notes.Add("Requires interaction to be enabled by game state.");
            var incoming = rows.Where(d => row.Scene.StartsWith("Interior ", StringComparison.Ordinal) && d.Class == "DoorChangelevel" && !d.Test && !Background(d) && d.Enabled && d.Active && d.Fields.String("OtherLevel") == row.Scene).ToList();
            // A named main entrance establishes the shop's public route. Gold shop's
            // two balcony transitions are unreachable from the street; their unlocked
            // defaults must not turn its weekday front-door schedule into 24-hour access.
            if (incoming.Any(d => EntrypointHas(d, "main"))) incoming.RemoveAll(d => EntrypointHas(d, "balcony"));
            foreach (var door in incoming)
            {
                var gates = controls.Where(c => c.Kind == "Door" && c.Target == door.Key).Concat(ObjectControls(door)).ToList();
                bool locked = door.Fields.GetProperty("_Lock").Bool("Locked");
                var entrance = new VendorEntrance { Name = TenantLocations.SceneLabel(door.Scene) + " → " + vendor.Location, Source = door.Key, InitiallyLocked = locked, Hours = gates.Count > 0 ? VendorHours.Combine(gates.Select(c => c.Hours), true) : locked ? null : VendorHours.AllDay(), Evidence = [door.Key + " → " + row.Scene] };
                foreach (var g in gates) { entrance.Evidence.AddRange(g.Evidence); entrance.Notes.AddRange(g.Notes); }
                if (locked && gates.Count == 0) entrance.Notes.Add("Entrance starts locked; no paired clock-driven unlock/lock was found. A key or game-state change is required.");
                if (!door.Active && !ObjectControls(door).Any()) entrance.Notes.Add("Entrance is inactive in the scene's starting state.");
                vendor.Entrances.Add(entrance);
            }
            // A physical shop door can be in the same scene as its vendor. Join
            // exact named shop groups, independently of scene-change entrances.
            var shopNames = row.Hierarchy.Split(" -> ").Append(vendor.Name).Select(Canonical).Where(n => n.Length > 7 && n is not "vendingmachine" and not "furnitureshop").ToHashSet();
            // A room can use the proprietor's surname while the menu lives under
            // their NPC hierarchy (Möbelmann / Möbelmann Furnitures / Hank Möbelmann).
            // Require that name in BOTH the public shop title and actual operator;
            // matching an arbitrary nearby NPC or a generic "Furniture" group is unsafe.
            shopNames.UnionWith(NameWords(vendor.Name).Where(n => n.Length > 6).Intersect(NameWords(vendor.Operator)));
            var localDoors = rows.Where(d => d.File == row.File && d.Class == "Door" && !d.Test && !Background(d) && d.Hierarchy.Split(" -> ").Select(Canonical).Any(shopNames.Contains)).ToList();
            foreach (var door in localDoors)
            {
                var gates = controls.Where(c => c.Kind == "Door" && c.Target == door.Key).ToList(); if (gates.Count == 0) continue;
                vendor.InternalAccess.Add(new() { Name = "Internal shop door: " + door.Hierarchy.Split(" -> ")[0], Source = door.Key, Hours = VendorHours.Combine(gates.Select(g => g.Hours), true), Evidence = gates.SelectMany(g => g.Evidence).Distinct().ToList(), Notes = gates.SelectMany(g => g.Notes).Distinct().ToList() });
            }
            if (vendor.Entrances.Count == 0 && row.Scene.StartsWith("Interior ", StringComparison.Ordinal)) vendor.Notes.Add("Requires access to this interior; no scene-change entrance was found in the scanned scenes.");
            vendor.Notes = vendor.Notes.Distinct().ToList(); vendor.Evidence = vendor.Evidence.Distinct().ToList(); result.Vendors.Add(vendor);
        }
        result.Vendors = result.Vendors.OrderBy(v => v.Location).ThenBy(v => v.Name).ThenBy(v => v.Id).ToList();
        return result;
    }

    public static async Task<VendorCatalog> LoadAsync(string repository, string gameRoot, string dataDirectory, Catalog catalog, IProgress<string>? progress, CancellationToken cancel)
    {
        var validation = catalog.FileValidation ??= new GameFileValidation(gameRoot, dataDirectory);
        var cached = Path.Combine(dataDirectory, "vendors.json"); var baseline = Path.Combine(repository, "docs/evidence/vendors.json");
        VendorCatalog? data = File.Exists(cached) ? Json.Read<VendorCatalog>(cached) : File.Exists(baseline) ? Json.Read<VendorCatalog>(baseline) : null;
        if (data?.FormatVersion != VendorCatalog.CurrentFormatVersion && File.Exists(baseline)) data = Json.Read<VendorCatalog>(baseline);
        bool matches = data?.FormatVersion == VendorCatalog.CurrentFormatVersion;
        if (data != null)
        {
            var sources = Directory.EnumerateFiles(Path.Combine(gameRoot, "Obenseuer_Data")).Select(Path.GetFileName).Where(f => f!.EndsWith(".assets", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(f, "^level[0-9]+$")).ToHashSet();
            matches &= sources.SetEquals(data.Inputs.Select(i => i.File).Where(f => f.EndsWith(".assets", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(f, "^level[0-9]+$")));
            foreach (var input in data.Inputs)
            {
                if (!matches) break; cancel.ThrowIfCancellationRequested(); progress?.Report("Verifying vendor schedules: " + input.File);
                var hash = await validation.HashAsync(input.File, cancel);
                matches &= hash == input.Hash;
            }
        }
        if (matches) { validation.Save(); return data!; }
        var staging = Path.Combine(dataDirectory, "vendors-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("Reading vendors, entrance locks, and opening-hours relay graphs...");
            await CatalogImport.RunScript(repository, "Export-Vendors.ps1", gameRoot, staging, cancel);
            data = Build(Json.Read<List<VendorRecord>>(Path.Combine(staging, "vendor-records.json")).Concat(Json.Read<List<VendorRecord>>(Path.Combine(staging, "vendor-extra-records.json"))).Concat(Json.Read<List<VendorRecord>>(Path.Combine(staging, "vendor-labels.json"))).ToList(), Json.Read<List<InputHash>>(Path.Combine(staging, "vendor-inputs.json")), Json.Read<List<JsonElement>>(Path.Combine(staging, "vendor-scan.json")).Count);
            AtomicFile.Write(cached, JsonSerializer.Serialize(data, Json.Options)); validation.Save(); return data;
        }
        finally { if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(staging, true); }
    }
}
