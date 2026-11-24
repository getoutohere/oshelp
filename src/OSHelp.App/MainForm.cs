using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm : Form
{
    readonly string repository, gameRoot, dataDirectory;
    readonly ProfileStore store;
    readonly CancellationTokenSource cancellation = new();
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
    readonly Panel content = new() { Dock = DockStyle.Fill };
    readonly DarkCombo profiles = Theme.Combo(230), apartments = Theme.Combo(210);
    readonly Dictionary<string, Control> pages = [];
    readonly Dictionary<string, Button> navigation = [];
    Catalog catalog = null!;
    Profile profile = null!;
    bool refreshing, loaded;
    string activePage = "Tenants";
    DataGridView tenantGrid = null!, requirementsGrid = null!, upgradeGrid = null!, materialGrid = null!, jobGrid = null!, stateGrid = null!;
    TextBox tenantSearch = null!, upgradeSearch = null!;
    DarkViewer upgradeDetails = null!, contributors = null!;
    Label tenantHeading = null!, tenantInfo = null!, tenantResult = null!, cartTotals = null!, stateInfo = null!;
    Label apartmentRental = null!, tenantRevenue = null!, tenantRevenueChange = null!, tenantMonthly = null!, tenantRevenueNote = null!;
    Button planTenant = null!, removeCompletedButton = null!;
    DarkCombo recruit = null!, permission = null!, categoryFilter = null!, levelEditor = null!, occupant = null!;
    CheckBox buildingUpgrades = null!, allUpgrades = null!, buildingState = null!;
    DarkNumber resources = null!, money = null!, speed = null!;
    DarkCombo preset = null!;
    CheckBox ignoreCosts = null!, furnitureOnly = null!, movingIn = null!;
    public string? SmokeDirectory { get; init; }

    sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    sealed record ApartmentChoice(int Number, string Name) { public override string ToString() => Name; }
    sealed record LevelChoice(int? Level, string Name) { public override string ToString() => Name; }
    sealed record FactChoice(bool? Value, string Name) { public override string ToString() => Name; }

    public MainForm(string repository, string gameRoot, string dataDirectory)
    {
        this.repository = repository; this.gameRoot = gameRoot; this.dataDirectory = dataDirectory;
        store = new(Path.Combine(dataDirectory, "profiles"));
        Text = "OSHelp - Obenseuer Tools"; ClientSize = new(1480, 900); MinimumSize = new(1120, 740);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
        Theme.Apply(this); content.BackColor = Theme.C("window"); status.BackColor = Theme.C("recess"); status.ForeColor = Theme.C("muted"); status.Padding = new(12, 0, 0, 0);
        Controls.Add(content); Controls.Add(status);
        var heading = Theme.Flow(DockStyle.Top, 62); heading.BackColor = Theme.C("window");
        heading.AutoSize = true; heading.AutoSizeMode = AutoSizeMode.GrowAndShrink; heading.MinimumSize = new(0, 62);
        heading.Controls.Add(Theme.Text("OSHELP", true)); heading.Controls.Add(Theme.Text("Profile:")); heading.Controls.Add(profiles);
        heading.Controls.Add(Theme.Button("New", (_, _) => NewProfile())); heading.Controls.Add(Theme.Button("Rename", (_, _) => RenameProfile()));
        deleteProfileButton = Theme.Button("Delete", (_, _) => DeleteProfile()); heading.Controls.Add(deleteProfileButton);
        syncLatestButton = Theme.Button("Sync latest save", async (_, _) => await SyncLatestSave());
        heading.Controls.Add(Theme.Button("Import save", (_, _) => ImportGameSave())); heading.Controls.Add(syncLatestButton); heading.Controls.Add(Theme.Button("Import profile", (_, _) => ImportProfile())); heading.Controls.Add(Theme.Button("Export", (_, _) => ExportProfile()));
        Controls.Add(heading);
        var nav = Theme.Flow(DockStyle.Top, 50); nav.BackColor = Theme.C("window");
        nav.AutoSize = true; nav.AutoSizeMode = AutoSizeMode.GrowAndShrink; nav.MinimumSize = new(0, 50);
        foreach (var name in new[] { "Tenants", "Upgrades", "Shopping Cart", "Crafting", "State", "Vendors", "Data" }) { var page = name; var button = Theme.Button(name.ToUpperInvariant(), (_, _) => ShowPage(page)); navigation[page] = button; nav.Controls.Add(button); }
        nav.Controls.Add(Theme.Text("Apartment:")); nav.Controls.Add(apartments); Controls.Add(nav);
        Controls.SetChildIndex(heading, Controls.Count - 1);
        status.Text = "Reading installed game data...";
        profiles.SelectedIndexChanged += (_, _) => { if (!refreshing && loaded && profiles.SelectedItem is Choice choice) Guard(() => SelectProfile(Guid.Parse(choice.Id))); };
        apartments.SelectedIndexChanged += (_, _) => { if (!refreshing && loaded) RefreshViews(); };
        Shown += async (_, _) => await Initialize();
        FormClosed += (_, _) => { cancellation.Cancel(); store.Dispose(); cancellation.Dispose(); };
        HandleCreated += (_, _) => Theme.DarkCaption(this);
    }
    int Apartment => (apartments.SelectedItem as ApartmentChoice)?.Number ?? 4;
    Tenant? SelectedTenant => tenantGrid?.CurrentRow?.Tag as Tenant;
    Upgrade? SelectedUpgrade => upgradeGrid?.CurrentRow?.Tag as Upgrade;
    Job? SelectedJob => jobGrid?.CurrentRow?.Tag as Job;
    void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { Dialogs.Message(this, "OSHelp", e.Message); status.Text = "Action not saved: " + e.Message; }
    }
    async Task Initialize()
    {
        var startup = Stopwatch.StartNew();
        bool reporting = true;
        try
        {
            var progress = new Progress<string>(text => { if (!IsDisposed && reporting && !loaded) status.Text = text; });
            catalog = await CatalogImport.LoadAsync(repository, gameRoot, dataDirectory, false, progress, cancellation.Token);
            vendorCatalog = await VendorImport.LoadAsync(repository, gameRoot, dataDirectory, catalog, progress, cancellation.Token);
            var dataSeconds = startup.Elapsed.TotalSeconds;
            refreshing = true;
            foreach (var a in catalog.Apartments) apartments.Items.Add(new ApartmentChoice(a.Number, $"{a.Number:00} / {catalog.ApartmentType(a.Number).Name}"));
            apartments.SelectedItem = apartments.Items.Cast<ApartmentChoice>().First(a => a.Number == 4);
            BuildPages();
            var existing = store.List();
            if (SmokeDirectory != null) { profile = Profile.Create(catalog, "UI bootstrap"); store.Save(profile); }
            else if (existing.Count > 0)
            {
                Guid wanted = existing[0].Id;
                try { var settings = Json.Read<Dictionary<string, Guid>>(Path.Combine(dataDirectory, "settings.json")); if (settings.TryGetValue("activeProfile", out var id) && existing.Any(p => p.Id == id)) wanted = id; } catch (Exception e) when (e is IOException or JsonException) { }
                try { profile = store.Load(wanted); }
                catch (Exception e)
                {
                    if (!Dialogs.Message(this, "Profile recovery", "The profile could not be loaded:\r\n" + e.Message + "\r\n\r\nAttempt to restore its backup?", true)) throw;
                    profile = store.Load(wanted, true); store.Save(profile, true);
                }
                if (profile.CatalogFingerprint != catalog.Fingerprint)
                {
                    if (!Dialogs.Message(this, "Changed game data", "The saved profile belongs to a different game catalog. Its plan is preserved. Create a separate profile for the current installation?", true)) throw new InvalidDataException("The previous profile has been preserved. Restart with its original game data or a separate --data-dir.");
                    profile = Profile.Create(catalog, "Updated game playthrough"); store.Save(profile);
                }
                CheckCatalog(profile);
            }
            else { profile = Profile.Create(catalog, "My playthrough"); store.Save(profile); }
            loaded = true; refreshing = false; RefreshProfiles(); RefreshViews(); ShowPage(activePage);
            status.Text = $"Saved / {catalog.Tenants.Count} resident definitions / {catalog.Upgrades.Count} upgrade definitions / Game files verified / Import save or record playthrough state";
            if (SmokeDirectory != null)
            {
                Directory.CreateDirectory(SmokeDirectory);
                File.WriteAllText(Path.Combine(SmokeDirectory, "startup.json"), JsonSerializer.Serialize(new {
                    dataSeconds, readySeconds = startup.Elapsed.TotalSeconds,
                    filesHashed = catalog.FileValidation!.FilesHashed, bytesHashed = catalog.FileValidation.BytesHashed
                }, Json.Options));
                await SmokeCapture();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            refreshing = false; status.Text = "Initialization failed. Game files and existing profiles were not modified.";
            var panel = Theme.Panel(); var text = new DarkViewer { Dock = DockStyle.Fill, Text = e.ToString() }; panel.Controls.Add(text); content.Controls.Add(panel);
            if (SmokeDirectory != null) { Directory.CreateDirectory(SmokeDirectory); File.WriteAllText(Path.Combine(SmokeDirectory, "error.txt"), e.ToString()); Close(); }
        }
        finally { reporting = false; }
    }
    void CheckCatalog(Profile p)
    {
        if (p.CatalogFingerprint != catalog.Fingerprint) throw new InvalidDataException("This profile was made with a different game catalog. Its saved plan has been preserved. Export it and create a new profile for this installation before changing construction quantities.");
        ProfileStore.Validate(p);
        foreach (var job in p.Jobs) _ = catalog.Upgrade(job.UpgradeId);
        bool changed = RentalIncome.RestoreImportedFacts(p, catalog);
        changed |= Planner.ReconcileUnderway(catalog, p);
        if (changed) store.Save(p);
    }
    void Mutate(Action<Profile> change, bool redraw = true)
    {
        var next = ProfileStore.Clone(profile); change(next); store.Save(next); profile = next;
        status.Text = "Saved " + DateTime.Now.ToString("HH:mm:ss") + " / " + profile.Name + " / Planner changes do not modify the game";
        if (redraw) RefreshViews();
    }
    void RefreshProfiles()
    {
        refreshing = true; profiles.Items.Clear(); foreach (var p in store.List()) profiles.Items.Add(new Choice(p.Id.ToString(), p.Name));
        profiles.SelectedItem = profiles.Items.Cast<Choice>().FirstOrDefault(x => x.Id == profile.Id.ToString()); refreshing = false;
        AtomicFile.Write(Path.Combine(dataDirectory, "settings.json"), JsonSerializer.Serialize(new Dictionary<string, Guid> { ["activeProfile"] = profile.Id }, Json.Options));
    }
    void SelectProfile(Guid id) { try { var next = store.Load(id); CheckCatalog(next); profile = next; RefreshProfiles(); RefreshViews(); status.Text = "Loaded " + profile.Name; } catch { RefreshProfiles(); throw; } }
    void NewProfile()
    {
        if (!loaded) return; var name = Dialogs.Prompt(this, "New playthrough", "Profile name", "New playthrough"); if (string.IsNullOrWhiteSpace(name)) return;
        Guard(() => { var next = Profile.Create(catalog, name); store.Save(next); profile = next; RefreshProfiles(); RefreshViews(); });
    }
    void RenameProfile()
    {
        if (!loaded) return; var name = Dialogs.Prompt(this, "Rename profile", "Profile name", profile.Name); if (string.IsNullOrWhiteSpace(name)) return;
        Guard(() => { Mutate(p => p.Name = name); RefreshProfiles(); });
    }
    void ExportProfile()
    {
        if (!loaded) return; using var dialog = new SaveFileDialog { Filter = "OSHelp profile (*.json)|*.json", FileName = "oshelp-profile.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) Guard(() => AtomicFile.Write(dialog.FileName, JsonSerializer.Serialize(profile, Json.Options)));
    }
    void ImportProfile()
    {
        if (!loaded) return; using var dialog = new OpenFileDialog { Filter = "OSHelp profile (*.json)|*.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) Guard(() => { profile = store.Import(dialog.FileName, catalog); CheckCatalog(profile); RefreshProfiles(); RefreshViews(); });
    }
    void ImportGameSave()
    {
        if (!loaded) return;
        Guard(() =>
        {
            var imported = SaveImportDialog.Show(this, catalog); if (imported == null) return;
            var saved = SaveImportedProfile(imported); if (saved == null) return;
            profile = saved; RefreshProfiles(); RefreshViews(); ShowPage("State");
            status.Text = "Imported " + profile.Name + " / " + profile.ImportedSave!.SaveDate.LocalDateTime.ToString("g") + " / Game save preserved";
        });
    }
    Profile? SaveImportedProfile(Profile imported, bool? automatedOverwrite = null)
    {
        var matches = store.FindNameMatches(imported.Name); var target = ProfileStore.ChooseImportTarget(matches, profile.Id);
        if (target != null && !SaveImportDialog.ConfirmOverwrite(this, imported, target, matches.Count, SmokeDirectory, automatedOverwrite))
        { status.Text = "Import cancelled. Existing profiles were not changed."; return null; }
        return store.SaveGameImport(imported, matches, target?.Id);
    }
    static DarkSplit Split(bool horizontal = false) => new() { Size = new(1200, 650), Dock = DockStyle.Fill, Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical, BackColor = Theme.C("window") };
    static void Attach(Control parent, Control fill, Control? top = null, Control? bottom = null)
    {
        parent.Controls.Add(fill); if (bottom != null) parent.Controls.Add(bottom); if (top != null) parent.Controls.Add(top);
    }
    static void AddRow(DataGridView grid, object tag, params object?[] values) { var i = grid.Rows.Add(values); grid.Rows[i].Tag = tag; }
    static void Retain(DataGridView grid, object? tag) { if (tag == null) return; foreach (DataGridViewRow row in grid.Rows) if (Equals(row.Tag, tag)) { grid.CurrentCell = row.Cells[0]; return; } }
    void BuildPages()
    {
        pages["Tenants"] = BuildTenants(); pages["Upgrades"] = BuildUpgrades(); pages["Shopping Cart"] = BuildCart(); pages["Crafting"] = BuildCrafting(); pages["State"] = BuildState(); pages["Vendors"] = BuildVendors(); pages["Data"] = BuildData();
        foreach (var page in pages.Values) { page.Dock = DockStyle.Fill; content.Controls.Add(page); }
    }
    void ShowPage(string name)
    {
        activePage = name; if (!loaded) return;
        foreach (var page in pages) page.Value.Visible = page.Key == name;
        foreach (var button in navigation) button.Value.BackColor = Theme.C(button.Key == name ? "button" : "raised");
        pages[name].BringToFront();
        if (name == "Crafting")
        {
            craftingSplit.SplitterDistance = (int)(craftingSplit.Width * .62);
            if (craftingSearch.Text.Trim().Length > 0 && craftingRenderedRevision != craftingRevision) ScheduleCraftingSearch();
        }
        if (name == "Vendors") vendorSplit.SplitterDistance = Math.Max(150, (int)(vendorSplit.Height * .42));
        if (pages[name] is DarkSplit split) split.SplitterDistance = name == "Tenants" ? Math.Clamp((int)(split.Width * .47), 500, 850) : name == "Vendors" ? Math.Max(200, (int)(split.Height * .44)) : Math.Max(200, split.Height / 2);
    }
    void RefreshViews()
    {
        if (!loaded) return; refreshing = true;
        try { RefreshTenants(); RefreshUpgrades(); RefreshCart(); RefreshState(); RefreshVendors(); }
        finally { refreshing = false; }
    }
    Control BuildTenants()
    {
        var split = Split(); var left = Theme.Panel(); var right = Theme.Panel(); split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right);
        tenantGrid = Theme.Grid(("name", "Resident", 26), ("size", "Sizes", 14), ("rental", "Rental status", 25), ("location", "Offer location", 35)); tenantGrid.CurrentCellChanged += (_, _) => { if (!refreshing) RenderTenant(); };
        tenantGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        tenantGrid.RowsDefaultCellStyle.WrapMode = tenantGrid.AlternatingRowsDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        tenantGrid.RowTemplate.Height = 56;
        tenantSearch = Theme.Input(); tenantSearch.Dock = DockStyle.Top; tenantSearch.PlaceholderText = "Search residents or household..."; tenantSearch.TextChanged += (_, _) => RefreshTenants();
        var searchPanel = Theme.Panel(DockStyle.Top, 0); searchPanel.Height = 58;
        var searchLabel = Theme.Text("Search names, household, rental status, or location"); searchLabel.Dock = DockStyle.Top; searchLabel.Font = Theme.Body;
        tenantSearch.Dock = DockStyle.Bottom; searchPanel.Controls.Add(tenantSearch); searchPanel.Controls.Add(searchLabel);
        Attach(left, tenantGrid, BuildTenantFilters(searchPanel));
        requirementsGrid = Theme.Grid(("name", "Requirement", 27), ("required", "Minimum / rule", 29), ("current", "Profile state", 29), ("met", "Result", 15));
        var header = Theme.Panel(DockStyle.Top); header.Height = 305;
        tenantHeading = Theme.Text("", true); tenantHeading.Dock = DockStyle.Top;
        tenantInfo = Theme.Text(""); tenantInfo.Dock = DockStyle.Top; tenantInfo.AutoSize = false; tenantInfo.Height = 72;
        apartmentRental = Theme.Text(""); apartmentRental.Dock = DockStyle.Top; apartmentRental.AutoSize = false; apartmentRental.Height = 30; apartmentRental.ForeColor = Theme.C("accent");
        tenantRevenue = Theme.Text(""); tenantRevenue.Dock = DockStyle.Top; tenantRevenue.AutoSize = false; tenantRevenue.Height = 45;
        tenantRevenueChange = Theme.Text(""); tenantRevenueChange.Dock = DockStyle.Top; tenantRevenueChange.AutoSize = false; tenantRevenueChange.Height = 28;
        tenantMonthly = Theme.Text(""); tenantMonthly.Dock = DockStyle.Top; tenantMonthly.AutoSize = false; tenantMonthly.Height = 35; tenantMonthly.ForeColor = Theme.C("muted");
        tenantRevenueNote = Theme.Text(""); tenantRevenueNote.Dock = DockStyle.Fill; tenantRevenueNote.AutoSize = false; tenantRevenueNote.ForeColor = Theme.C("muted");
        header.Controls.Add(tenantRevenueNote); header.Controls.Add(tenantMonthly); header.Controls.Add(tenantRevenueChange); header.Controls.Add(tenantRevenue); header.Controls.Add(apartmentRental); header.Controls.Add(tenantInfo); header.Controls.Add(tenantHeading);
        var bottom = Theme.Panel(DockStyle.Bottom); bottom.Height = 154;
        tenantResult = Theme.Text(""); tenantResult.Dock = DockStyle.Top; tenantResult.AutoSize = false; tenantResult.Height = 42;
        var actions = Theme.Flow(DockStyle.Bottom, 55);
        planTenant = Theme.Button("Plan missing work", (_, _) => Guard(PlanForTenant)); actions.Controls.Add(planTenant);
        actions.Controls.Add(Theme.Button("Source + recruitment", (_, _) => Guard(TenantSource)));
        var facts = Theme.Flow(DockStyle.Fill, 45); facts.Controls.Add(Theme.Text("Recruitment offer reached:")); recruit = FactCombo();
        recruit.SelectedIndexChanged += (_, _) => { if (!refreshing && SelectedTenant is { } t && recruit.SelectedItem is FactChoice v) Guard(() => Mutate(p => p.Recruitment[t.Id] = v.Value)); };
        facts.Controls.Add(recruit); bottom.Controls.Add(facts); bottom.Controls.Add(tenantResult); bottom.Controls.Add(actions);
        Attach(right, requirementsGrid, header, bottom); return split;
    }
    void RefreshTenants()
    {
        if (tenantGrid == null || profile == null) return;
        var selected = SelectedTenant?.Id; bool old = refreshing; refreshing = true;
        tenantGrid.Rows.Clear();
        var sizes = Enumerable.Range(0, tenantSizes.Length).Where(i => tenantSizes[i].Checked);
        foreach (var t in TenantDirectory.Filter(catalog, profile, sizes, (TenantRentalFilter)Math.Max(0, tenantRental.SelectedIndex), tenantSearch.Text))
        {
            var rented = TenantRentalStatus.Apartments(catalog, profile, t).Count > 0;
            AddRow(tenantGrid, t, t.Name, string.Join(" / ", t.Sizes.Select(GameRules.SizeName)), TenantRentalStatus.Label(catalog, profile, t).Replace(" · ", "\r\n", StringComparison.Ordinal), t.LocationHint);
            tenantGrid.Rows[^1].Cells["rental"].Style.ForeColor = Theme.C(rented ? "success" : "muted");
            if (rented) tenantGrid.Rows[^1].Cells["name"].Style.ForeColor = Theme.C("success");
        }
        if (tenantSort is { } sort) tenantGrid.Sort(tenantGrid.Columns[sort.Column], sort.Direction);
        foreach (DataGridViewRow row in tenantGrid.Rows) if ((row.Tag as Tenant)?.Id == selected) { tenantGrid.CurrentCell = row.Cells[0]; break; }
        refreshing = old; RenderTenant();
    }
    void RenderTenant()
    {
        if (profile == null || requirementsGrid == null) return; requirementsGrid.Rows.Clear(); var t = SelectedTenant;
        planTenant.Enabled = t != null && Apartment != 1 && catalog.ApartmentType(Apartment).HasRentalUi && t.Sizes.Contains(catalog.ApartmentType(Apartment).Size);
        if (t == null) { tenantHeading.Text = "No matching resident"; tenantInfo.Text = ""; tenantResult.Text = ""; apartmentRental.Text = RentalIncome.ApartmentStatus(catalog, profile, Apartment); tenantRevenue.Text = "Select a resident to compare rental revenue."; tenantMonthly.Text = tenantRevenueChange.Text = tenantRevenueNote.Text = ""; return; }
        tenantHeading.Text = t.Name.ToUpper(CultureInfo.CurrentCulture);
        var classification = t.InitialApartment.HasValue ? "Initially resident in apartment " + t.InitialApartment : t.Recruitment.Count > 0 ? "Recruitment offer found in game dialogue" : "Special resident definition; no recruitment offer found";
        var rentedApartments = TenantRentalStatus.Apartments(catalog, profile, t);
        tenantInfo.Text = $"{(rentedApartments.Count > 0 ? TenantRentalStatus.Label(catalog, profile, t) : classification)}\r\nOffer location: {t.LocationHint}\r\nHousehold: {string.Join(", ", new[] { t.Name }.Concat(t.Household.Select(h => h.Name)))}";
        var comparison = RentalIncome.Compare(catalog, profile, t, Apartment); apartmentRental.Text = comparison.Occupancy;
        string Amount(RentPayment? amount) => amount.HasValue ? $"{amount.Value.Oc:N0} OC + {amount.Value.Rm:#,0.##} RM / {comparison.PaymentPeriod}" : "Not recorded";
        static string Monthly(MonthlyRent? amount) => amount.HasValue ? $"{amount.Value.Oc:N0} OC + {amount.Value.Rm:N0} RM" : "not recorded";
        tenantRevenue.Text = Apartment == 1 || !catalog.ApartmentType(Apartment).HasRentalUi ? "Rental revenue: not applicable." : $"Current payment: {Amount(comparison.CurrentPayment)}\r\nWith {t.Name}: {Amount(comparison.SelectedPayment)}";
        tenantRevenueChange.Text = comparison.PaymentChange is RentPayment change ? $"Change: {change.Oc:+#,0;-#,0;0} OC · {change.Rm:+#,0.##;-#,0.##;0} RM / {comparison.PaymentPeriod}" : "";
        tenantRevenueChange.ForeColor = Theme.C(comparison.PaymentChange is not RentPayment delta || (delta.Oc == 0 && delta.Rm == 0) ? "muted" : delta.Oc >= 0 && delta.Rm >= 0 ? "success" : delta.Oc <= 0 && delta.Rm <= 0 ? "danger" : "warning");
        tenantMonthly.Text = comparison.Selected.HasValue ? $"Monthly totals before payment rounding: current {Monthly(comparison.Current)}; proposed {Monthly(comparison.Selected)}" : "";
        tenantRevenueNote.Text = comparison.Note;
        var results = GameRules.Evaluate(catalog, profile, t, Apartment);
        foreach (var r in results)
        {
            AddRow(requirementsGrid, r, r.Name, r.Required, r.Recorded, r.Met == true ? "Met" : r.Met == false ? "Not met" : "Unrecorded");
            requirementsGrid.Rows[^1].Cells[3].Style.ForeColor = Theme.C(r.Met == true ? "success" : r.Met == false ? "danger" : "warning");
        }
        bool old = refreshing; refreshing = true; SetFact(recruit, profile.Recruitment.GetValueOrDefault(t.Id)); refreshing = old;
        var first = results.FirstOrDefault(r => r.Met == false);
        tenantResult.Text = first != null ? "Not eligible: " + first.Name + "." : results.Any(r => r.Met == null) ? "Record the remaining profile facts to evaluate this rental." : "All listed rental requirements are met by your recorded profile.";
        tenantResult.ForeColor = Theme.C(first != null ? "warning" : results.Any(r => r.Met == null) ? "muted" : "success");
    }
    void PlanForTenant()
    {
        if (SelectedTenant is not { } t) return;
        var goals = Planner.ForTenant(catalog, profile, t, Apartment);
        if (Dialogs.Plan(this, catalog, profile, goals)) { Mutate(p => Planner.Confirm(catalog, p, goals)); ShowPage("Shopping Cart"); }
    }
    void TenantSource()
    {
        if (SelectedTenant is not { } t) return;
        var text = $"GAME SOURCE: {t.Source.Location}\r\n{t.Id}\r\n\r\n";
        text += "OFFER LOCATION: " + t.LocationHint + "\r\n" + string.Join("\r\n", t.Placements.Select(p => $"{p.Scene} / {p.SourceFile}:{p.PathId} / {p.Class}{(p.TestPlacement ? " [test placement excluded from hint]" : "")}\r\n  {p.Hierarchy}")) + "\r\n\r\n";
        text += t.DisableEviction ? "Eviction is disabled for this resident.\r\n" : "";
        text += t.DialogueActor.Length > 0 ? "Renovation permission is overridden by dialogue actor: " + t.DialogueActor + ".CanUpgradeApartment\r\n" : t.NoUpgrade ? "Resident restricts renovations.\r\n" : "";
        text += "\r\nRental permission item: " + catalog.PermissionItem + ". CanRentApartment also checks that this NPC is not already renting.\r\n";
        text += "\r\nDIRECT RENTAL OFFERS\r\n" + string.Join("\r\n\r\n", t.Recruitment.Select(e => e.Conversation + " / entry " + e.EntryId + "\r\n" + e.Text + "\r\nCondition: " + e.Conditions + "\r\n" + e.Source));
        text += "\r\n\r\nCONVERSATION CONTEXT (conditions can occur on earlier nodes)\r\n" + t.RecruitmentContext;
        text += "\r\n\r\nRAW RESIDENT DEFINITION\r\n" + JsonSerializer.Serialize(t.Source.Fields, Json.Options);
        Dialogs.Message(this, t.Name + " - actual game sources", text);
    }
    Control BuildUpgrades()
    {
        var split = Split(true); var top = Theme.Panel(); var lower = Theme.Panel(); split.Panel1.Controls.Add(top); split.Panel2.Controls.Add(lower);
        upgradeGrid = Theme.Grid(("name", "Upgrade", 41), ("category", "Category", 19), ("tier", "Level", 12), ("start", "Start OC", 9), ("final", "Final RM", 9), ("availability", "Available here", 17));
        upgradeGrid.CurrentCellChanged += (_, _) => { if (!refreshing) RenderUpgrade(); };
        var filters = Theme.Flow(DockStyle.Top, 50); buildingUpgrades = Check("Building", false); allUpgrades = Check("All definitions", false); upgradeSearch = Theme.Input("", 210); upgradeSearch.PlaceholderText = "Search upgrades...";
        categoryFilter = Theme.Combo(190); categoryFilter.Items.Add(new Choice("", "All categories")); foreach (var c in Categories.All) categoryFilter.Items.Add(new Choice(c.Key, c.Name)); categoryFilter.SelectedIndex = 0;
        filters.Controls.AddRange([buildingUpgrades, allUpgrades, categoryFilter, Theme.Text("Search:"), upgradeSearch]);
        buildingUpgrades.CheckedChanged += (_, _) => RefreshUpgrades(); allUpgrades.CheckedChanged += (_, _) => RefreshUpgrades(); categoryFilter.SelectedIndexChanged += (_, _) => RefreshUpgrades(); upgradeSearch.TextChanged += (_, _) => RefreshUpgrades();
        Attach(top, upgradeGrid, filters);
        upgradeDetails = new DarkViewer { Dock = DockStyle.Fill, Font = Theme.Number };
        var actions = Theme.Flow(DockStyle.Bottom, 55); actions.Controls.Add(Theme.Button("Add selected work", (_, _) => Guard(AddUpgrade))); actions.Controls.Add(Theme.Button("Raw source", (_, _) => { if (SelectedUpgrade is { } u) Dialogs.Message(this, u.Name, u.Source.Location + "\r\n" + u.Id + "\r\n\r\n" + JsonSerializer.Serialize(u.Source.Fields, Json.Options)); }));
        Attach(lower, upgradeDetails, null, actions); return split;
    }
    void RefreshUpgrades()
    {
        if (upgradeGrid == null || profile == null) return;
        var selected = SelectedUpgrade?.Id; bool old = refreshing; refreshing = true; upgradeGrid.Rows.Clear();
        string filter = (categoryFilter.SelectedItem as Choice)?.Id ?? "";
        foreach (var u in catalog.Upgrades.OrderBy(u => u.CategoryName).ThenBy(u => u.Tier).ThenBy(u => u.Name))
        {
            bool applies = catalog.Applicable(u, Apartment); bool qualifies = catalog.ContractorsFor(u, u.Building ? 0 : Apartment).Any();
            if (!allUpgrades.Checked && (!applies || !qualifies || u.Tier == 0 || u.Building != buildingUpgrades.Checked)) continue;
            if (filter.Length > 0 && !u.Effects.Any(e => e.Category == filter)) continue;
            if (!(u.Name + " " + u.CategoryName).Contains(upgradeSearch.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
            AddRow(upgradeGrid, u, catalog.UpgradeLabel(u), u.CategoryName, string.Join(", ", u.Effects.Select(e => catalog.TierName(e.Category, e.Tier))), profile.IgnoreCosts ? 0 : GameRules.Price(u.StartOc, profile.MoneyMultiplier), profile.IgnoreCosts ? 0 : GameRules.Price(u.FinalRm, profile.MoneyMultiplier), u.Tier == 0 ? "Starting state" : !applies ? "Other target" : !qualifies ? "No contractor" : "Yes");
        }
        foreach (DataGridViewRow row in upgradeGrid.Rows) if ((row.Tag as Upgrade)?.Id == selected) { upgradeGrid.CurrentCell = row.Cells[0]; break; }
        refreshing = old; RenderUpgrade();
    }
    void RenderUpgrade()
    {
        if (upgradeDetails == null || profile == null) return;
        if (SelectedUpgrade is not { } u) { upgradeDetails.Text = "No matching upgrades. Turn on All definitions to inspect starting or unavailable states."; return; }
        var text = new StringBuilder().AppendLine(catalog.UpgradeLabel(u)).AppendLine($"Target: {(u.Building ? "Building" : "Apartment " + Apartment)} / {u.CategoryName}").AppendLine();
        text.AppendLine($"Start: {(profile.IgnoreCosts ? 0 : GameRules.Price(u.StartOc, profile.MoneyMultiplier)):N0} OC / Final: {(profile.IgnoreCosts ? 0 : GameRules.Price(u.FinalRm, profile.MoneyMultiplier)):N0} RM / Time: {GameRules.Hours(u.Hours, profile.SpeedMultiplier):N0} game hours");
        text.AppendLine("Qualified contractors: " + string.Join(", ", catalog.ContractorsFor(u, u.Building ? 0 : Apartment).Select(c => c.Name)));
        text.AppendLine().AppendLine("MATERIALS (each line adjusted before summing)");
        foreach (var m in u.Materials) text.AppendLine($"  {(profile.IgnoreCosts ? 0 : GameRules.MaterialCost(m.Amount, profile.ResourceMultiplier)),6}  {catalog.Items[m.ItemKey].Name}   [base {m.Amount}, {m.ItemKey}]");
        if (u.Materials.Count == 0) text.AppendLine("  No material entries. Availability still depends on target and contractor.");
        text.AppendLine().AppendLine("EXPLICIT PREREQUISITES");
        foreach (var id in u.Prerequisites) { var dep = catalog.Upgrade(id); text.AppendLine("  " + catalog.UpgradeLabel(dep) + " / " + (GameRules.Satisfied(dep, Apartment, profile) is true ? "already met" : "needed or unrecorded")); }
        if (u.Prerequisites.Count == 0) text.AppendLine("  None. Lower tiers are not automatically added.");
        text.AppendLine().AppendLine("Source: " + u.Source.Location).AppendLine(u.Id); upgradeDetails.Text = text.ToString();
    }
    void AddUpgrade()
    {
        if (SelectedUpgrade is not { } u) return;
        var goals = Planner.AddGoal(catalog, profile, u, Apartment);
        if (Dialogs.Plan(this, catalog, profile, goals)) { Mutate(p => Planner.Confirm(catalog, p, goals)); ShowPage("Shopping Cart"); }
    }
    Control BuildCart()
    {
        var split = Split(true); var top = Theme.Panel(); var lower = Theme.Panel(); split.Panel1.Controls.Add(top); split.Panel2.Controls.Add(lower);
        jobGrid = Theme.Grid(("target", "Target", 17), ("job", "Confirmed construction", 48), ("status", "Status", 20), ("role", "Reason", 15));
        var actions = Theme.Flow(DockStyle.Bottom, 56);
        actions.AutoSize = true; actions.AutoSizeMode = AutoSizeMode.GrowAndShrink; actions.MinimumSize = new(0, 56);
        actions.Controls.Add(Theme.Button("Remove goal", (_, _) => Guard(RemoveGoal)));
        actions.Controls.Add(Theme.Button("Start / consume stock", (_, _) => Guard(() => TransitionJob(false))));
        actions.Controls.Add(Theme.Button("Record completed", (_, _) => Guard(() => TransitionJob(true))));
        removeCompletedButton = Theme.Button("Remove completed", (_, _) => Guard(RemoveCompletedJobs));
        actions.Controls.Add(removeCompletedButton);
        actions.Controls.Add(Theme.Button("Already underway", (_, _) => Guard(() =>
        {
            if (SelectedJob is not { } selected) return;
            if (Dialogs.Message(this, "Record work already underway", "Record that this job had already started in the game before you entered it in OSHelp? Its materials will leave the shopping list. Current unspent stock will not be deducted again.", true))
                Mutate(p => Planner.RecordUnderway(catalog, p, p.Jobs.Single(j => j.Key == selected.Key)));
        })));
        actions.Controls.Add(Theme.Button("Export list", (_, _) => Guard(ExportCart)));
        Attach(top, jobGrid, null, actions);
        var detailSplit = Split(); detailSplit.SplitterDistance = 650;
        materialGrid = Theme.Grid(("material", "Material", 48), ("need", "Required", 17), ("have", "Have (edit)", 17), ("buy", "To buy", 18));
        materialGrid.ReadOnly = false; foreach (DataGridViewColumn c in materialGrid.Columns) c.ReadOnly = c.Name != "have";
        materialGrid.Columns["have"].DefaultCellStyle.BackColor = Theme.C("raised");
        materialGrid.CellValidating += (_, e) => { if (e.ColumnIndex == materialGrid.Columns["have"].Index && !refreshing && (!long.TryParse(Convert.ToString(e.FormattedValue), out var n) || n < 0 || n > int.MaxValue)) { e.Cancel = true; status.Text = "Have must be a whole number between 0 and 2,147,483,647."; } };
        materialGrid.CellEndEdit += (_, e) => { if (refreshing || materialGrid.Rows[e.RowIndex].Tag is not CartLine line) return; Guard(() => { var n = Convert.ToInt64(materialGrid.Rows[e.RowIndex].Cells["have"].Value); Mutate(p => p.Stock[line.ItemKey] = n, false); BeginInvoke((Action)RefreshCart); }); };
        contributors = new DarkViewer { Dock = DockStyle.Fill };
        materialGrid.CurrentCellChanged += (_, _) => RenderContributors();
        detailSplit.Panel1.Controls.Add(materialGrid); detailSplit.Panel2.Controls.Add(contributors);
        cartTotals = Theme.Text(""); cartTotals.Dock = DockStyle.Top; cartTotals.AutoSize = false; cartTotals.Height = 50;
        var stockActions = Theme.Flow(DockStyle.Bottom, 48);
        stockActions.Controls.Add(Theme.Button("Record all required stock on hand", (_, _) => Guard(() => { if (Dialogs.Message(this, "Record current stock", "Set current unspent stock to at least the amount required by every planned job? Use this only if you have collected those materials in the game.", true)) Mutate(p => { foreach (var line in Planner.Cart(catalog, p)) p.Stock[line.ItemKey] = Math.Max(line.Have, line.Required); }); })));
        Attach(lower, detailSplit, cartTotals, stockActions); return split;
    }
    void RefreshCart()
    {
        if (jobGrid == null || profile == null) return;
        var selected = SelectedJob?.Key; var selectedItem = (materialGrid.CurrentRow?.Tag as CartLine)?.ItemKey; var selectedColumn = materialGrid.CurrentCell?.ColumnIndex ?? 0; bool old = refreshing; refreshing = true; jobGrid.Rows.Clear();
        foreach (var job in profile.Jobs.OrderBy(j => j.Status).ThenBy(j => j.Apartment).ThenBy(j => catalog.Upgrade(j.UpgradeId).Name))
            AddRow(jobGrid, job, catalog.ScopeName(job.Apartment), catalog.UpgradeLabel(catalog.Upgrade(job.UpgradeId)), job.Status == JobStatus.UnderConstruction ? job.ReadyForPayment ? "Awaiting final payment" : "Under construction" : job.Status.ToString(), profile.Goals.Any(g => g.UpgradeId == job.UpgradeId && g.Apartment == job.Apartment) ? "Selected goal" : job.Status == JobStatus.Planned ? "Prerequisite" : job.Status == JobStatus.UnderConstruction ? job.Contractor : "History");
        foreach (DataGridViewRow row in jobGrid.Rows) if ((row.Tag as Job)?.Key == selected) { jobGrid.CurrentCell = row.Cells[0]; break; }
        materialGrid.Rows.Clear(); var lines = Planner.Cart(catalog, profile);
        foreach (var line in lines) { AddRow(materialGrid, line, catalog.Items[line.ItemKey].Name, line.Required, line.Have, line.Remaining); materialGrid.Rows[^1].Cells["buy"].Style.ForeColor = Theme.C(line.Remaining == 0 ? "success" : "accent"); }
        foreach (DataGridViewRow row in materialGrid.Rows) if ((row.Tag as CartLine)?.ItemKey == selectedItem) { materialGrid.CurrentCell = row.Cells[selectedColumn]; break; }
        long start = profile.Jobs.Where(j => j.Status == JobStatus.Planned).Sum(j => (long)GameRules.Price(catalog.Upgrade(j.UpgradeId).StartOc, profile.MoneyMultiplier));
        long final = profile.Jobs.Where(j => j.Status != JobStatus.Completed).Sum(j => (long)GameRules.Price(catalog.Upgrade(j.UpgradeId).FinalRm, profile.MoneyMultiplier));
        cartTotals.Text = $"{profile.Jobs.Count(j => j.Status == JobStatus.Planned)} planned jobs / {lines.Count} materials / Start: {(profile.IgnoreCosts ? 0 : start):N0} OC / Final: {(profile.IgnoreCosts ? 0 : final):N0} RM\r\nHave means current unspent stock; it is deducted once across all planned jobs.";
        if (lines.Count == 0) contributors.Text = "No outstanding materials. Add work from Tenants or Upgrades. Under-construction and completed jobs do not require materials again.";
        removeCompletedButton.Enabled = profile.Jobs.Any(j => j.Status == JobStatus.Completed);
        refreshing = old; RenderContributors();
    }
    void RenderContributors() { if (contributors != null && materialGrid?.CurrentRow?.Tag is CartLine line) contributors.Text = catalog.Items[line.ItemKey].Name + "\r\n" + line.ItemKey + "\r\n\r\n" + string.Join("\r\n\r\n", line.Contributors); }
    void RemoveCompletedJobs()
    {
        if (!profile.Jobs.Any(j => j.Status == JobStatus.Completed)) return;
        int removed = 0;
        Mutate(p => removed = Planner.RemoveCompleted(p));
        status.Text = $"Removed {removed} completed job{(removed == 1 ? "" : "s")} / Saved {profile.Name}";
    }
    void RemoveGoal()
    {
        if (SelectedJob is not { } job) return;
        if (job.Status != JobStatus.Planned) throw new InvalidOperationException("Started/completed work is preserved as history. Removing a plan does not undo construction or stock consumption.");
        if (!profile.Goals.Any(g => g.UpgradeId == job.UpgradeId && g.Apartment == job.Apartment)) throw new InvalidOperationException("This is a shared prerequisite. Remove the goal that requires it, or record the existing tier in State.");
        var goals = profile.Goals.Where(g => g.UpgradeId != job.UpgradeId || g.Apartment != job.Apartment).ToArray();
        if (Dialogs.Message(this, "Remove construction goal", "Remove this selected goal and recalculate prerequisites? Shared work required by other goals will remain.", true)) Mutate(p => Planner.Confirm(catalog, p, goals));
    }
    void TransitionJob(bool complete)
    {
        if (SelectedJob is not { } selected) return;
        var text = complete ? "Record this job as completed in your game? Its target levels will be updated in the profile." : "Record that this construction has started in your game? The planner will deduct its materials from recorded stock and remove them from the outstanding shopping list.";
        if (!Dialogs.Message(this, complete ? "Complete work" : "Start work", text + "\r\n\r\n" + catalog.Upgrade(selected.UpgradeId).Name, true)) return;
        Mutate(p => { var job = p.Jobs.Single(j => j.Key == selected.Key); if (complete) Planner.Complete(catalog, p, job); else Planner.Start(catalog, p, job); });
    }
    void ExportCart()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV shopping list (*.csv)|*.csv|Text list (*.txt)|*.txt", FileName = "oshelp-shopping-list.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var lines = Planner.Cart(catalog, profile);
        var text = dialog.FilterIndex == 1 ? "Material,Required,Have,To buy,Jobs\r\n" + string.Join("\r\n", lines.Select(l => $"{Q(catalog.Items[l.ItemKey].Name)},{l.Required},{l.Have},{l.Remaining},{Q(string.Join("; ", l.Contributors))}"))
            : profile.Name + "\r\n" + string.Join("\r\n", lines.Select(l => $"{l.Remaining,6} to buy / {l.Required,6} required / {l.Have,6} on hand: {catalog.Items[l.ItemKey].Name}"));
        AtomicFile.Write(dialog.FileName, text);
    }
    static CheckBox Check(string text, bool value) => new DarkCheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Theme.C("text"), BackColor = Theme.C("panel"), Font = Theme.Body, FlatStyle = FlatStyle.Flat, Margin = new(6, 10, 14, 4) };
    static DarkCombo FactCombo()
    {
        var combo = Theme.Combo(160); combo.Items.AddRange([new FactChoice(null, "Not recorded"), new FactChoice(false, "Not yet / No"), new FactChoice(true, "Confirmed / Yes")]); combo.SelectedIndex = 0; return combo;
    }
    static void SetFact(DarkCombo combo, bool? value) => combo.SelectedItem = combo.Items.Cast<FactChoice>().Single(x => x.Value == value);
    static DarkNumber Multiplier(decimal minimum)
    {
        return new DarkNumber { Minimum = minimum, Maximum = 4, Value = 1, Width = 80, Margin = new(4, 8, 10, 4) };
    }
    Control BuildState()
    {
        var page = Theme.Panel(); var lower = Theme.Panel(DockStyle.Bottom); lower.Height = 230;
        stateGrid = Theme.Grid(("name", "Facility / upgrade", 45), ("current", "Recorded current level", 55)); stateGrid.CurrentCellChanged += (_, _) => RenderLevelEditor();
        var header = Theme.Flow(DockStyle.Top, 48); buildingState = Check("Building facilities", false); buildingState.CheckedChanged += (_, _) => RefreshState();
        header.Controls.Add(buildingState); levelEditor = Theme.Combo(240); header.Controls.Add(levelEditor); header.Controls.Add(Theme.Button("Record level", (_, _) => Guard(RecordLevel)));
        header.Controls.Add(Theme.Button("New-game defaults", (_, _) => Guard(RecordDefaults)));
        stateInfo = Theme.Text(""); stateInfo.Dock = DockStyle.Top; stateInfo.AutoSize = false; stateInfo.Height = 54;
        var top = Theme.Panel(DockStyle.Fill); Attach(top, stateGrid, header, stateInfo); page.Controls.Add(top);
        var facts = Theme.Flow(DockStyle.Top, 50); permission = FactCombo(); facts.Controls.Add(Theme.Text("Have Tenement blueprint:")); facts.Controls.Add(permission);
        permission.SelectedIndexChanged += (_, _) => { if (!refreshing && permission.SelectedItem is FactChoice f) Guard(() => Mutate(p => p.HasRentalPermission = f.Value)); };
        occupant = Theme.Combo(240); occupant.Items.Add(new Choice("?", "Occupancy unrecorded")); occupant.Items.Add(new Choice("", "Vacant")); foreach (var t in catalog.Tenants.OrderBy(t => t.Name)) occupant.Items.Add(new Choice(t.Id, t.Name));
        occupant.SelectedIndexChanged += (_, _) => { if (!refreshing && occupant.SelectedItem is Choice c) Guard(() => Mutate(p => { if ((Apartment == 1 || catalog.ApartmentType(Apartment).Size == 3) && c.Id is not ("" or "?")) throw new InvalidOperationException("Player apartment and shop cannot be rented to tenants."); p.Apartment(Apartment).Occupant = c.Id == "?" ? null : c.Id; p.Apartment(Apartment).MovingIn = false; })); };
        facts.Controls.Add(Theme.Text("Selected apartment:")); facts.Controls.Add(occupant); furnitureOnly = Check("Furniture only", false); facts.Controls.Add(furnitureOnly);
        furnitureOnly.CheckedChanged += (_, _) => { if (!refreshing) Guard(() => Mutate(p => p.Apartment(Apartment).OnlyFurniture = furnitureOnly.Checked)); };
        movingIn = Check("Moving in", false); facts.Controls.Add(movingIn); movingIn.CheckedChanged += (_, _) => { if (!refreshing) Guard(() => Mutate(p => p.Apartment(Apartment).MovingIn = movingIn.Checked)); };
        lower.Controls.Add(facts);
        var settings = Theme.Flow(DockStyle.Top, 50); preset = Theme.Combo(150); foreach (var d in catalog.Presets) preset.Items.Add(d.Name); settings.Controls.Add(Theme.Text("Difficulty:")); settings.Controls.Add(preset);
        resources = Multiplier(0); money = Multiplier(0); speed = Multiplier(.25m);
        settings.Controls.Add(Theme.Text("Materials")); settings.Controls.Add(resources); settings.Controls.Add(Theme.Text("Money")); settings.Controls.Add(money); settings.Controls.Add(Theme.Text("Speed")); settings.Controls.Add(speed);
        settings.Controls.Add(Theme.Button("Apply settings", (_, _) => Guard(() => Mutate(p => { p.ResourceMultiplier = (float)resources.Value; p.MoneyMultiplier = (float)money.Value; p.SpeedMultiplier = (float)speed.Value; p.IgnoreCosts = ignoreCosts.Checked; if (preset.SelectedItem is string name && name != "Custom") { p.RentalIncomeMultiplier = catalog.RentalSettings.PresetMultiplier(name); p.RentPaymentMode = catalog.RentalSettings.PresetPaymentModes[name]; } }))));
        preset.SelectedIndexChanged += (_, _) => { if (!refreshing && preset.SelectedItem is string name) { var d = catalog.Presets.Single(x => x.Name == name); resources.Value = (decimal)d.Materials; money.Value = (decimal)d.Money; speed.Value = (decimal)d.Speed; } };
        lower.Controls.Add(settings); settings.BringToFront(); facts.BringToFront();
        var more = Theme.Flow(DockStyle.Bottom, 52); ignoreCosts = Check("Ignore all costs (creative/debug bypass)", false); more.Controls.Add(ignoreCosts);
        more.Controls.Add(Theme.Button("Apartment readiness + renovation permission", (_, _) => Guard(EditReadiness))); lower.Controls.Add(more);
        more.Controls.Add(Theme.Button("Rental income + payment schedule", (_, _) => Guard(() =>
        {
            var settings = Dialogs.RentalSettings(this, profile);
            if (settings is { } rent) Mutate(p => { p.RentalIncomeMultiplier = rent.Income; p.RentPaymentMode = rent.Mode; });
        })));
        var explanation = Theme.Text("Import a game save or record your current state here. Game saves are never modified.\r\nBlank levels are unrecorded; confirming a construction plan does not claim those upgrades already exist."); explanation.Dock = DockStyle.Fill; explanation.AutoSize = false; lower.Controls.Add(explanation); explanation.BringToFront();
        page.Controls.Add(lower); return page;
    }
    void RefreshState()
    {
        if (stateGrid == null || profile == null) return; bool old = refreshing; refreshing = true;
        var selected = (stateGrid.CurrentRow?.Tag as Category)?.Key; stateGrid.Rows.Clear();
        foreach (var c in Categories.All.Where(c => c.Building == buildingState.Checked))
        {
            var level = profile.Level(c.Key, Apartment); AddRow(stateGrid, c, c.Name, level.HasValue ? catalog.TierName(c.Key, level.Value) : "Not recorded");
        }
        foreach (DataGridViewRow row in stateGrid.Rows) if ((row.Tag as Category)?.Key == selected) { stateGrid.CurrentCell = row.Cells[0]; break; }
        SetFact(permission, profile.HasRentalPermission);
        occupant.Enabled = Apartment != 1 && catalog.ApartmentType(Apartment).HasRentalUi;
        var a = profile.Apartment(Apartment); occupant.SelectedItem = occupant.Items.Cast<Choice>().FirstOrDefault(c => c.Id == (a.Occupant ?? "?")); furnitureOnly.Checked = a.OnlyFurniture;
        movingIn.Checked = a.MovingIn;
        resources.Value = (decimal)profile.ResourceMultiplier; money.Value = (decimal)profile.MoneyMultiplier; speed.Value = (decimal)profile.SpeedMultiplier; ignoreCosts.Checked = profile.IgnoreCosts;
        preset.SelectedItem = catalog.Presets.FirstOrDefault(p => p.Name != "Custom" && p.Materials == profile.ResourceMultiplier && p.Money == profile.MoneyMultiplier && p.Speed == profile.SpeedMultiplier && catalog.RentalSettings.PresetMultiplier(p.Name) == profile.RentalIncomeMultiplier && catalog.RentalSettings.PresetPaymentModes[p.Name] == profile.RentPaymentMode)?.Name ?? "Custom";
        stateInfo.Text = buildingState.Checked ? "Building levels apply once to every apartment. Shared building work is counted once." : $"Apartment {Apartment}, floor {catalog.Apartments.Single(x => x.Number == Apartment).Floor} / {catalog.ApartmentType(Apartment).Name}\r\n{(Apartment == 1 ? "Player apartment: cannot rent to tenants." : catalog.ApartmentType(Apartment).Size == 3 ? "Shop: renovations are supported; cannot rent to tenants." : "Rental apartment")}";
        refreshing = old; RenderLevelEditor();
    }
    void RenderLevelEditor()
    {
        if (levelEditor == null || stateGrid.CurrentRow?.Tag is not Category c) return;
        bool old = refreshing; refreshing = true; levelEditor.Items.Clear(); levelEditor.Items.Add(new LevelChoice(null, "Not recorded"));
        foreach (var e in catalog.Enums["UpgradeType+" + c.Enum].OrderBy(x => x.Key)) levelEditor.Items.Add(new LevelChoice(e.Key, e.Value));
        levelEditor.SelectedItem = levelEditor.Items.Cast<LevelChoice>().Single(x => x.Level == profile.Level(c.Key, Apartment)); refreshing = old;
    }
    void RecordLevel()
    {
        if (stateGrid.CurrentRow?.Tag is not Category c || levelEditor.SelectedItem is not LevelChoice choice) return;
        Mutate(p => { p.SetLevel(c.Key, Apartment, choice.Level); Planner.Confirm(catalog, p, p.Goals.ToArray()); });
    }
    void RecordDefaults()
    {
        if (!Dialogs.Message(this, "Use new-game scene defaults", "Replace recorded facility levels and occupancy with the initial scene values extracted from the game? This is only appropriate for a fresh playthrough. Existing goals will be recalculated.", true)) return;
        Mutate(p => { p.BuildingCurrentUpgradeIds.Clear(); foreach (var c in catalog.InitialBuildingLevels) p.BuildingLevels[c.Key] = c.Value; foreach (var a in catalog.Apartments) { var state = p.Apartment(a.Number); state.CurrentUpgradeIds.Clear(); state.MovingIn = false; foreach (var value in a.InitialLevels) state.Levels[value.Key] = value.Value; state.Occupant = a.InitialTenant ?? ""; } Planner.Confirm(catalog, p, p.Goals.ToArray()); });
    }
    void EditReadiness()
    {
        using var f = Dialogs.Window("Apartment readiness", new(740, 420)); var form = Theme.Panel(); var state = profile.Apartment(Apartment);
        var examined = FactCombo(); SetFact(examined, state.Examined); var renovation = FactCombo(); SetFact(renovation, state.CanRenovate);
        var primary = Theme.Input(state.PrimaryCleared?.ToString() ?? "", 100); var secondary = Theme.Input(state.SecondaryCleared?.ToString() ?? "", 100);
        var rows = Theme.Flow(DockStyle.Top, 220); rows.FlowDirection = FlowDirection.TopDown; rows.WrapContents = false;
        var r1 = Theme.Flow(DockStyle.None, 45); r1.Width = 650; r1.Controls.AddRange([Theme.Text("Examined:"), examined]);
        var r2 = Theme.Flow(DockStyle.None, 45); r2.Width = 650; r2.Controls.AddRange([Theme.Text("Primary furniture cleared %:"), primary]);
        var r3 = Theme.Flow(DockStyle.None, 45); r3.Width = 650; r3.Controls.AddRange([Theme.Text("Secondary furniture cleared %:"), secondary]);
        var r4 = Theme.Flow(DockStyle.None, 45); r4.Width = 650; r4.Controls.AddRange([Theme.Text("Resident permits renovations:"), renovation]); rows.Controls.AddRange([r1, r2, r3, r4]); form.Controls.Add(rows);
        var note = Theme.Text("These record game facts, not changes to the game. Clearing thresholds are\r\n90% primary and 80% secondary where the game's clearing branch applies."); note.Dock = DockStyle.Bottom; form.Controls.Add(note); f.Controls.Add(form);
        var buttons = Theme.Flow(DockStyle.Bottom, 54); buttons.Controls.Add(Theme.Button("Save", (_, _) => Guard(() =>
        {
            static int? Percent(string text) => string.IsNullOrWhiteSpace(text) ? null : int.TryParse(text, out var n) && n is >= 0 and <= 100 ? n : throw new InvalidOperationException("Clearance must be 0 to 100, or blank.");
            int? p1 = Percent(primary.Text), p2 = Percent(secondary.Text);
            Mutate(p => { var a = p.Apartment(Apartment); a.Examined = ((FactChoice)examined.SelectedItem!).Value; a.CanRenovate = ((FactChoice)renovation.SelectedItem!).Value; a.PrimaryCleared = p1; a.SecondaryCleared = p2; }); f.DialogResult = DialogResult.OK;
        }))); buttons.Controls.Add(Theme.Button("Cancel", (_, _) => f.DialogResult = DialogResult.Cancel)); f.Controls.Add(buttons); f.ShowDialog(this);
    }
    Control BuildData()
    {
        var page = Theme.Panel(); var text = new DarkViewer { Dock = DockStyle.Fill, Font = Theme.Number };
        var summary = new StringBuilder().AppendLine("VERIFIED INSTALLED GAME DATA").AppendLine().AppendLine(gameRoot).AppendLine()
            .AppendLine($"{catalog.Tenants.Count} resident definitions / {catalog.Tenants.Count(t => t.Recruitment.Count > 0)} residents with StartRent dialogue callbacks")
            .AppendLine($"{catalog.Upgrades.Count} upgrades / {catalog.Upgrades.Sum(u => u.Materials.Count)} material lines / {catalog.Apartments.Count} scene-defined apartments")
            .AppendLine($"{catalog.Contractors.Count} contractors / permission item: {catalog.PermissionItem}").AppendLine()
            .AppendLine($"{vendorCatalog.Vendors.Count} vendor/menu locations / {vendorCatalog.FilesScanned} asset files checked for vendors")
            .AppendLine("Costs use the game's per-material-line float32 multiplication, integer truncation, and minimum-one rule.")
            .AppendLine("All definitions include starting/unavailable tiers. An empty bill does not make a tier constructible.")
            .AppendLine("The player apartment and shop cannot be rented to tenants. Their upgrades remain browsable.")
            .AppendLine("Import save reads a snapshot into a new profile. Further changes are tracked manually; game saves are never modified.")
            .AppendLine("Recruitment callback conditions and their conversation context are available under Source + recruitment.")
            .AppendLine("Tenant revenue comparisons use current upgrade values, the game's currency-specific income rules, and rental income difficulty multiplier.")
            .AppendLine().AppendLine("RUNTIME: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)
            .AppendLine("DATA: " + dataDirectory).AppendLine("CATALOG: " + catalog.Fingerprint).AppendLine().AppendLine("INPUT HASHES");
        foreach (var i in catalog.Inputs) summary.AppendLine(i.File + "\r\n  " + i.Hash);
        text.Text = summary.ToString(); page.Controls.Add(text);
        var actions = Theme.Flow(DockStyle.Bottom, 54); actions.Controls.Add(Theme.Button("Open data folder", (_, _) => Guard(() => Process.Start(new ProcessStartInfo(dataDirectory) { UseShellExecute = true }))));
        actions.Controls.Add(Theme.Button("Imported save details", (_, _) =>
        {
            var source = profile.ImportedSave;
            Dialogs.Message(this, "Imported save source", source == null ? "This profile was entered manually." : source.Character + " / " + source.Slot + " / Game " + source.Version + "\r\nSaved: " + source.SaveDate.LocalDateTime.ToString("g") + "\r\nImported: " + source.ImportedAt.LocalDateTime.ToString("g") + "\r\n" + source.Directory + "\r\n\r\nStock sources:\r\n" + string.Join("\r\n", source.StockSources) + "\r\n\r\n" + string.Join("\r\n", source.Files.Select(f => f.File + ": " + f.Hash)));
        }));
        actions.Controls.Add(Theme.Button("Refresh game catalog", async (_, _) => { if (!Dialogs.Message(this, "Refresh catalog", "Close OSHelp and restart after game data changes. Refresh now will verify and rebuild the catalog; profile quantities are never migrated silently.", true)) return; try { Enabled = false; var progress = new Progress<string>(s => status.Text = s); var updated = await CatalogImport.LoadAsync(repository, gameRoot, dataDirectory, true, progress, cancellation.Token); if (updated.Fingerprint != profile.CatalogFingerprint) throw new InvalidDataException("The refreshed game data differs from this profile. The profile is preserved; create a new profile after restarting."); var vendors = await VendorImport.LoadAsync(repository, gameRoot, dataDirectory, updated, progress, cancellation.Token); catalog = updated; vendorCatalog = vendors; ResetCraftingCatalog(); status.Text = "Catalog and vendor schedules rebuilt and verified."; RefreshViews(); } catch (Exception e) { Dialogs.Message(this, "Refresh failed", e.Message); } finally { Enabled = true; } }));
        page.Controls.Add(actions); return page;
    }
    async Task SmokeCapture()
    {
        Directory.CreateDirectory(SmokeDirectory!);
        // Isolate UI exercises from any real playthrough profile.
        var testProfile = Profile.Create(catalog, "UI verification");
        foreach (var key in testProfile.BuildingLevels.Keys) testProfile.BuildingLevels[key] = 0;
        foreach (var a in testProfile.Apartments) { foreach (var key in a.Levels.Keys) a.Levels[key] = 0; a.Occupant = ""; }
        testProfile.HasRentalPermission = true;
        var aleksi = catalog.Tenants.Single(t => t.Name == "Aleksi Kivi"); testProfile.Recruitment[aleksi.Id] = true;
        var goals = Planner.ForTenant(catalog, testProfile, aleksi, 4); Planner.Confirm(catalog, testProfile, goals);
        testProfile.Stock["game:35010"] = 10; store.Save(testProfile); profile = testProfile; RefreshProfiles(); RefreshViews();
        foreach (DataGridViewRow row in tenantGrid.Rows) if ((row.Tag as Tenant)?.Id == aleksi.Id) { tenantGrid.CurrentCell = row.Cells[0]; break; }
        if (tenantHeading.Text != aleksi.Name.ToUpper(CultureInfo.CurrentCulture)) throw new InvalidOperationException("Tenant selection did not update its detail view.");
        await Task.Delay(250);
        if (status.Text.StartsWith("Checking", StringComparison.Ordinal) || status.Text.StartsWith("Verifying", StringComparison.Ordinal)) throw new InvalidOperationException("Queued startup progress replaced the ready status.");
        await VerifyTenantFilters();
        foreach (var page in pages.Keys)
        {
            ShowPage(page); await Task.Delay(100);
            using var image = new Bitmap(Width, Height); DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, page.Replace(" ", "-") + ".png"));
        }
        ShowPage("Vendors"); vendorDay.SelectedIndex = 5; vendorTime.Text = "13:30"; vendorSearch.Text = "General Trader";
        if (vendorWeek.DisplayedRowCount(false) != 7) throw new InvalidOperationException("All seven vendor schedule days should be visible at the default window size.");
        if (vendorGrid.ColumnCount != 5 || vendorWeek.ColumnCount != 2 || vendorGrid.Rows.Count != 1 || SelectedVendor?.Operator != "Ivan Kapustanov" || SelectedVendor.Sources.Count != 2 || vendorGrid.Rows[0].Cells["hours"].Value?.ToString() != "08:00–12:00" || vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Closed") throw new InvalidOperationException("Combined vendor hours must show one General Trader row, closed Saturday 13:30.");
        using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Vendors-Saturday-closed.png")); }
        vendorOpenOnly.Checked = true; if (vendorGrid.Rows.Count != 0) throw new InvalidOperationException("Access-hours filter included a locked shop.");
        vendorOpenOnly.Checked = false; vendorTime.Text = "10:30";
        if (vendorGrid.Rows.Count != 1 || vendorGrid.Rows[0].Cells["status"].Value!.ToString() != "Open") throw new InvalidOperationException("Open vendor status did not refresh with selected game time.");
        vendorWeek.CurrentCell = vendorWeek.Rows[0].Cells[0]; await Task.Delay(100); if (vendorDay.SelectedIndex != 0) throw new InvalidOperationException("Weekly schedule selection did not change the chosen day.");
        vendorRegion.SelectedIndex = 2; vendorSearch.Text = "Alco"; if (vendorGrid.Rows.Count != 1) throw new InvalidOperationException("Alco checkout/trader duplicates were not merged.");
        vendorSearch.Text = "Bar Bizarre"; if (vendorGrid.Rows.Count != 1 || !SelectedVendor!.Operator.Contains("Anneli") || !SelectedVendor.Operator.Contains("Jooseppi")) throw new InvalidOperationException("Shared shop operators were not combined.");
        vendorSearch.Text = "Gold Shop"; vendorDay.SelectedIndex = 0; vendorTime.Text = "08:00";
        if (vendorGrid.Rows.Count != 1 || SelectedVendor?.Operator != "Torsten Dvergsson" || vendorGrid.Rows[0].Cells["hours"].Value?.ToString() != "08:00–16:00" || vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Open" || SelectedVendor.Sources.Single().Entrances.Count != 1)
            throw new InvalidOperationException("Gold shop must use its street entrance hours and omit inaccessible balconies.");
        vendorTime.Text = "16:00";
        if (vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Closed") throw new InvalidOperationException("Gold shop must close at 16:00.");
        vendorDay.SelectedIndex = 5; vendorTime.Text = "12:00";
        if (vendorGrid.Rows[0].Cells["hours"].Value?.ToString() != "Closed" || vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Closed") throw new InvalidOperationException("Gold shop must be closed on Saturday.");
        using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Vendors-Gold-weekend-closed.png")); }
        vendorOpenOnly.Checked = true;
        if (vendorGrid.Rows.Count != 0) throw new InvalidOperationException("Open-only filter included the closed gold shop.");
        vendorOpenOnly.Checked = false;
        File.WriteAllText(Path.Combine(SmokeDirectory!, "gold-shop.txt"), "PASS: weekday 08:00–16:00 / exact 16:00 close / closed Saturday / open-only filter / inaccessible balcony doors excluded.\n");
        vendorSearch.Text = "Welhö"; if (vendorGrid.Rows.Count != 1) throw new InvalidOperationException("Alternate stock menus were not merged.");
        vendorRegion.SelectedIndex = 1; vendorDay.SelectedIndex = 1; vendorTime.Text = "12:00"; vendorSearch.Text = "Hardware Stall";
        if (vendorGrid.Rows.Count != 1 || SelectedVendor?.Operator != "Henning Torskvær" || vendorGrid.Rows[0].Cells["name"].Value?.ToString() != "Hardware Stall" || vendorGrid.Rows[0].Cells["hours"].Value?.ToString() != "12:00–18:00" || vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Open") throw new InvalidOperationException("Hardware Stall must be searchable by its public name and open Tuesday at noon.");
        using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Vendors-Hardware-Tuesday.png")); }
        vendorOpenOnly.Checked = true; if (vendorGrid.Rows.Count != 1) throw new InvalidOperationException("Open-only filter hid the hardware stall at Tuesday noon.");
        vendorSearch.Text = "Henning's shop"; if (vendorGrid.Rows.Count != 1) throw new InvalidOperationException("Original hardware trader name is no longer searchable.");
        vendorOpenOnly.Checked = false;
        vendorSearch.Text = "Hank";
        foreach (int day in Enumerable.Range(0, 7))
        {
            vendorDay.SelectedIndex = day; vendorTime.Text = "08:00";
            if (vendorGrid.Rows.Count != 1 || SelectedVendor?.Name != "Möbelmann Furnitures" || vendorGrid.Rows[0].Cells["hours"].Value?.ToString() != "08:00–18:00" || vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Open")
                throw new InvalidOperationException("Möbelmann must use its interior door's 08:00–18:00 hours every day.");
        }
        vendorTime.Text = "07:59";
        if (vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Closed") throw new InvalidOperationException("Möbelmann opened before 08:00.");
        vendorTime.Text = "17:59";
        if (vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Open") throw new InvalidOperationException("Möbelmann closed before 18:00.");
        vendorTime.Text = "18:00";
        if (vendorGrid.Rows[0].Cells["status"].Value?.ToString() != "Closed" || !vendorNotes.Text.Contains("4 m") || !vendorNotes.Text.Contains("Closing may wait")) throw new InvalidOperationException("Möbelmann must close at 18:00 and explain the proximity delay.");
        using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Vendors-Mobelmann-interior-door.png")); }
        vendorOpenOnly.Checked = true;
        if (vendorGrid.Rows.Count != 0) throw new InvalidOperationException("Open-only filter included Möbelmann after its interior door closes.");
        vendorOpenOnly.Checked = false;
        File.WriteAllText(Path.Combine(SmokeDirectory!, "mobelmann.txt"), "PASS: interior shop door / 08:00–18:00 all seven days / exact opening and closing / nearby-player delay / open-only filter.\n");
        vendorSearch.Text = ""; vendorRegion.SelectedIndex = 3;
        if (vendorGrid.Rows.Count == 0 || vendorGrid.Rows.Cast<DataGridViewRow>().Any(r => !(r.Tag as VendorListing)!.InRegion("Mines"))) throw new InvalidOperationException("Vendor location filter failed.");
        vendorRegion.SelectedIndex = 0; vendorKind.SelectedIndex = 2;
        if (vendorGrid.Rows.Count == 0 || vendorGrid.Rows.Cast<DataGridViewRow>().Any(r => (r.Tag as VendorListing)!.Kind != "Vending machine")) throw new InvalidOperationException("Vending machine filter failed.");
        vendorKind.SelectedIndex = 0; vendorTime.Text = "bad time"; if (!vendorCount.Text.Contains("enter HH:mm")) throw new InvalidOperationException("Invalid game time was not handled.");
        vendorTime.Text = "12:00";
        await VerifyVendorSort();
        File.WriteAllText(Path.Combine(SmokeDirectory!, "vendor-sort.txt"), "PASS: every vendor column in both directions across all seven days, time, empty search, region/type/open filters and profile refresh; selection and sort glyph preserved; weekly table sort and day selection preserved.\n");
        ShowPage("Shopping Cart");
        foreach (DataGridViewRow row in materialGrid.Rows)
            if ((row.Tag as CartLine)?.ItemKey == "game:35010")
            {
                materialGrid.CurrentCell = row.Cells["have"];
                if (!materialGrid.BeginEdit(true) || materialGrid.EditingControl is not TextBox edit) throw new InvalidOperationException("Stock editor could not be opened.");
                edit.Text = "10"; materialGrid.EndEdit(); break;
            }
        await Task.Delay(100);
        var reloaded = store.Load(profile.Id);
        var plank = Planner.Cart(catalog, reloaded).Single(l => l.ItemKey == "game:35010");
        if (plank.Required != 32 || plank.Have != 10 || plank.Remaining != 22) throw new InvalidOperationException("UI edit/save/reload shopping cart verification failed.");
        ShowPage("State");
        foreach (DataGridViewRow row in stateGrid.Rows) if ((row.Tag as Category)?.Key == "walls") { stateGrid.CurrentCell = row.Cells[0]; break; }
        levelEditor.SelectedItem = levelEditor.Items.Cast<LevelChoice>().Single(l => l.Level == 1); RecordLevel();
        if (profile.Apartment(4).Levels["walls"] != 1 || profile.Apartment(4).Levels["power"] != 0) throw new InvalidOperationException("State editor updated the wrong facility.");
        if (profile.Jobs.Count(j => j.Status == JobStatus.Planned) != 1) throw new InvalidOperationException("Existing work did not leave the shopping cart.");
        levelEditor.SelectedItem = levelEditor.Items.Cast<LevelChoice>().Single(l => l.Level == 0); RecordLevel();
        preset.SelectedItem = "Casual";
        if (resources.Value != .5m || money.Value != .5m || speed.Value != 2) throw new InvalidOperationException("Difficulty preset editor does not match the game.");
        // Exercise the shop special case through the actual selector.
        apartments.SelectedItem = apartments.Items.Cast<ApartmentChoice>().Single(a => a.Number == 2); ShowPage("Tenants");
        if (GameRules.Evaluate(catalog, profile, aleksi, Apartment)[0].Met != false || planTenant.Enabled || occupant.Enabled) throw new InvalidOperationException("Shop rental UI check failed.");
        await Task.Delay(100);
        using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Shop.png")); }
        apartments.SelectedItem = apartments.Items.Cast<ApartmentChoice>().Single(a => a.Number == 4);
        Mutate(p => p.Apartment(4).Occupant = aleksi.Id);
        var aleksiRow = tenantGrid.Rows.Cast<DataGridViewRow>().Single(r => (r.Tag as Tenant)?.Id == aleksi.Id);
        if (!aleksiRow.Cells["rental"].Value!.ToString()!.Contains("Already renting") || !aleksiRow.Cells["rental"].Value!.ToString()!.Contains("04")) throw new InvalidOperationException("Current rental marker did not update from occupancy.");
        Mutate(p => p.Apartment(4).OnlyFurniture = true);
        if (tenantGrid.Rows.Cast<DataGridViewRow>().Single(r => (r.Tag as Tenant)?.Id == aleksi.Id).Cells["rental"].Value!.ToString()!.Contains("Already renting")) throw new InvalidOperationException("Furniture-only placement was labelled as an active rental.");
        Mutate(p => { p.Apartment(4).OnlyFurniture = false; p.Apartment(4).Occupant = ""; });
        tenantSearch.Text = "Deekula A";
        if (tenantGrid.Rows.Count != 1 || SelectedTenant?.Id != aleksi.Id || tenantGrid.CurrentRow!.Cells["location"].Value?.ToString() != "Deekula A") throw new InvalidOperationException("Location search or offer-location column failed.");
        tenantSearch.Text = "";
        var pate = catalog.Tenants.Single(t => t.Name == "Pate Rantanen");
        Mutate(p => p.Apartment(4).Occupant = pate.Id);
        if (!apartmentRental.Text.Contains(pate.Name) || !tenantRevenue.Text.Contains("364 OC") || !tenantRevenue.Text.Contains("864 OC") || !tenantRevenue.Text.Contains("/ week") || !tenantRevenueChange.Text.Contains("+500 OC") || !tenantMonthly.Text.Contains(1455.ToString("N0"))) throw new InvalidOperationException("Replacement revenue panel does not match the game's weekly Pate-to-Aleksi payments.");
        foreach (DataGridViewRow row in tenantGrid.Rows) if ((row.Tag as Tenant)?.Name == "Agnes Takala") { tenantGrid.CurrentCell = row.Cells[0]; break; }
        if (!tenantRevenueChange.Text.Contains("-238 OC") || !tenantRevenueChange.Text.Contains("+70 RM") || tenantRevenueChange.ForeColor != Theme.C("warning")) throw new InvalidOperationException("Currency-specific replacement gains/losses were not rendered.");
        apartments.SelectedItem = apartments.Items.Cast<ApartmentChoice>().Single(a => a.Number == 3);
        if (!apartmentRental.Text.Contains("Apartment 03: vacant") || !tenantRevenue.Text.Contains("158 OC + " + 73.25m.ToString("#,0.##") + " RM")) throw new InvalidOperationException("Apartment selection did not recalculate revenue.");
        apartments.SelectedItem = apartments.Items.Cast<ApartmentChoice>().Single(a => a.Number == 4);
        foreach (DataGridViewRow row in tenantGrid.Rows) if ((row.Tag as Tenant)?.Id == aleksi.Id) { tenantGrid.CurrentCell = row.Cells[0]; break; }
        ShowPage("Tenants"); await Task.Delay(100);
        using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Replacement-revenue.png")); }
        Mutate(p => p.Apartment(4).Occupant = "");
        if (SaveImport.Discover().Count > 0)
        {
            var imported = SaveImportDialog.Show(this, catalog, SmokeDirectory) ?? throw new InvalidOperationException("Save import dialog did not return a profile.");
            if (imported.Id == profile.Id || imported.ImportedSave?.StockSources.SingleOrDefault() != "Carried inventory + equipped items") throw new InvalidOperationException("Save source selection was not respected.");
            var committed = SaveImportedProfile(imported, true) ?? throw new InvalidOperationException("Initial save import was cancelled.");
            profile = store.Load(committed.Id); RefreshProfiles(); RefreshViews();
            foreach (var page in new[] { "Tenants", "State", "Shopping Cart" })
            {
                ShowPage(page); await Task.Delay(100); using var image = new Bitmap(Width, Height); DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Imported-" + page.Replace(" ", "-") + ".png"));
            }
            var priorId = profile.Id; var priorHash = CatalogImport.Hash(store.PathFor(priorId)); var priorCount = store.List().Count;
            var replacement = ProfileStore.Clone(imported); replacement.Stock["game:35010"] = profile.Stock.GetValueOrDefault("game:35010") + 1;
            if (SaveImportedProfile(replacement, false) != null || CatalogImport.Hash(store.PathFor(priorId)) != priorHash || store.List().Count != priorCount) throw new InvalidOperationException("Cancelling overwrite changed an existing profile.");
            var overwritten = SaveImportedProfile(replacement, true) ?? throw new InvalidOperationException("Confirmed overwrite was cancelled.");
            if (overwritten.Id != priorId || store.List().Count != priorCount || store.FindNameMatches(imported.Name).Count != 1 || CatalogImport.Hash(store.PathFor(priorId) + ".bak") != priorHash) throw new InvalidOperationException("Confirmed overwrite did not reuse the profile or preserve its backup.");
            profile = store.Load(overwritten.Id); RefreshProfiles(); RefreshViews();
        }
        File.WriteAllText(Path.Combine(SmokeDirectory!, "success.txt"), $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\nTenants: {catalog.Tenants.Count}\nUpgrades: {catalog.Upgrades.Count}\nApartments: {catalog.Apartments.Count}\nVendors: {vendorCatalog.Vendors.Count}\nVendor search / Saturday 13:30 locked entrance / trader-door overlap / access-only filter / chosen time refresh / all seven weekdays / weekly day selection / region filter / vending filter / invalid time: PASS\nTenant selection / rental annotations / furniture-only exclusion / scene location column and search / current renter and replacement revenue / currency-specific gains and losses / revenue refresh on apartment selection / stock editor / save and reload / facility editor / goal recalculation / Casual preset / shop exclusion / save picker / import review / source selection / imported profile reload / overwrite confirmation and cancellation / unique imported name / previous profile backup: PASS\n");
        await VerifyCraftingView();
        await VerifyCartCleanup();
        await SmokeProfileActions();
        Close();
    }
}
