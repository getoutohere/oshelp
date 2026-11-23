using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm
{
    Button deleteProfileButton = null!, syncLatestButton = null!;

    void DeleteProfile(bool? automatedResponse = null)
    {
        if (!loaded) return;
        var deleting = profile;
        if (!ProfileDialogs.Confirm(this, "Delete profile?", "Delete", $"Delete \"{deleting.Name}\" from OSHelp?\r\n\r\nIts recorded state and shopping plan will leave the profile list. A recovery copy is retained under profiles/deleted. Game saves are preserved.", SmokeDirectory, "Delete", automatedResponse)) return;
        Guard(() =>
        {
            var archive = store.Delete(deleting);
            Profile? next = null;
            foreach (var candidate in store.List())
            {
                try { var available = store.Load(candidate.Id); CheckCatalog(available); next = available; break; }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException) { }
            }
            if (next == null)
            {
                var name = "My playthrough"; int suffix = 2;
                while (store.FindNameMatches(name).Count > 0) name = "My playthrough " + suffix++;
                next = Profile.Create(catalog, name); store.Save(next);
            }
            profile = next; RefreshProfiles(); RefreshViews();
            status.Text = "Deleted " + deleting.Name + " / Recovery copy: " + archive;
        });
    }

    async Task SyncLatestSave(bool? automatedOlderResponse = null)
    {
        if (!loaded) return;
        var original = profile;
        var linked = original;
        if (linked.ImportedSave == null)
        {
            var selected = SaveImportDialog.Show(this, catalog, SmokeDirectory, forSync: true);
            if (selected == null) return;
            linked = ProfileStore.Clone(original); linked.ImportedSave = selected.ImportedSave;
        }
        try
        {
            Enabled = false; UseWaitCursor = true;
            status.Text = "Finding latest save for " + linked.ImportedSave!.Character.Replace('_', ' ') + "...";
            var result = await Task.Run(() =>
            {
                var latest = ProfileSync.Latest(linked);
                return SaveImport.Read(latest.Directory, catalog);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (original.ImportedSave != null && result.Profile.ImportedSave!.SaveDate < original.ImportedSave.SaveDate
                && !ProfileDialogs.Confirm(this, "Save is older than this profile", "Sync older save",
                    $"The latest available save is {result.Slot.Date.LocalDateTime:g}, older than the last import ({original.ImportedSave.SaveDate.LocalDateTime:g}).\r\n\r\nSync this older state? Your shopping goals will be retained and recalculated.", SmokeDirectory, "Older-sync", automatedOlderResponse))
            { status.Text = "Sync cancelled. Profile preserved."; return; }
            var updated = ProfileSync.Merge(catalog, linked, result);
            store.SaveSync(original, updated);
            profile = updated; RefreshProfiles(); RefreshViews();
            status.Text = $"Synced {profile.Name} / {result.Slot.Name} / {result.Slot.Date.LocalDateTime:g} / Shopping plan recalculated";
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (SmokeDirectory != null) throw;
            if (!IsDisposed) { status.Text = "Sync failed. Profile preserved."; Dialogs.Message(this, "Save sync", e.Message); }
        }
        finally { if (!IsDisposed) { Enabled = true; UseWaitCursor = false; } }
    }
    async Task SmokeProfileActions()
    {
        if (!deleteProfileButton.Visible || !syncLatestButton.Visible) throw new InvalidOperationException("Profile actions are not visible.");
        var heading = deleteProfileButton.Parent!;
        var originalSize = ClientSize;
        ClientSize = new(1120, 740); PerformLayout();
        if (!heading.ClientRectangle.Contains(deleteProfileButton.Bounds) || !heading.ClientRectangle.Contains(syncLatestButton.Bounds)) throw new InvalidOperationException("Profile actions are clipped at the minimum window size.");
        ClientSize = originalSize; PerformLayout();
        if (SaveImport.Discover().Count > 0)
        {
            var manual = Profile.Create(catalog, "Sync keeps my custom profile name"); store.Save(manual); profile = manual; RefreshProfiles(); RefreshViews();
            var id = profile.Id; var count = store.List().Count;
            await SyncLatestSave(true);
            if (profile.Id != id || profile.Name != manual.Name || profile.ImportedSave == null || store.List().Count != count || profile.ImportedSave.StockSourceIds?.SingleOrDefault() != "inventory")
                throw new InvalidOperationException("Linking and syncing did not preserve the selected profile and stock choices.");
            var priorHash = CatalogImport.Hash(store.PathFor(id)); var revision = profile.Revision;
            await SyncLatestSave(true);
            if (profile.Revision != revision + 1 || CatalogImport.Hash(store.PathFor(id) + ".bak") != priorHash || store.List().Count != count)
                throw new InvalidOperationException("Sync did not update in place with the previous profile as backup.");
            Mutate(p => p.ImportedSave!.SaveDate = DateTimeOffset.UtcNow.AddYears(1));
            priorHash = CatalogImport.Hash(store.PathFor(id));
            await SyncLatestSave(false);
            if (CatalogImport.Hash(store.PathFor(id)) != priorHash) throw new InvalidOperationException("Cancelling an older save sync changed the profile.");
            await SyncLatestSave(true);
        }
        var deletingId = profile.Id; var before = CatalogImport.Hash(store.PathFor(deletingId)); var beforeCount = store.List().Count;
        DeleteProfile(false);
        if (profile.Id != deletingId || CatalogImport.Hash(store.PathFor(deletingId)) != before || store.List().Count != beforeCount) throw new InvalidOperationException("Cancelling profile deletion changed saved data.");
        DeleteProfile(true);
        if (profile.Id == deletingId || File.Exists(store.PathFor(deletingId)) || store.List().Count != Math.Max(1, beforeCount - 1)) throw new InvalidOperationException("Confirmed deletion did not select a remaining profile.");
        // This store belongs exclusively to the smoke run. Exercise last-profile deletion too.
        var last = profile;
        foreach (var entry in store.List().Where(p => p.Id != last.Id)) store.Delete(store.Load(entry.Id));
        profile = store.Load(last.Id); RefreshProfiles();
        DeleteProfile(true);
        if (store.List().Count != 1 || profile.Id == last.Id || profile.ImportedSave != null || profile.Goals.Count != 0) throw new InvalidOperationException("Deleting the last profile did not create an empty playthrough.");
        RefreshViews(); ShowPage("State"); await Task.Delay(100);
        using var screenshot = new Bitmap(Width, Height); DrawToBitmap(screenshot, new(0, 0, Width, Height)); screenshot.Save(Path.Combine(SmokeDirectory!, "Profile-actions.png"));
        File.WriteAllText(Path.Combine(SmokeDirectory!, "profile-actions.txt"), "PASS: first-time character linking / latest save sync / renamed profile identity / selected stock sources / in-place revision and backup / older-save confirmation and cancellation / delete confirmation and cancellation / remaining profile selection / last profile replacement / controls at minimum window size.\n");
    }
}

internal static class ProfileDialogs
{
    public static bool Confirm(IWin32Window owner, string title, string action, string text, string? smokeDirectory, string capture, bool? automatedResponse = null)
    {
        using var window = Dialogs.Window(title, new(800, 440));
        window.Controls.Add(new DarkViewer { Dock = DockStyle.Fill, Text = text });
        var actions = Theme.Flow(DockStyle.Bottom, 58);
        var confirm = Theme.Button(action, (_, _) => window.DialogResult = DialogResult.OK);
        var cancel = Theme.Button("Cancel", (_, _) => window.DialogResult = DialogResult.Cancel);
        actions.Controls.AddRange([confirm, cancel]); window.Controls.Add(actions);
        window.AcceptButton = cancel; window.CancelButton = cancel; window.ActiveControl = cancel;
        if (automatedResponse.HasValue) window.Shown += async (_, _) =>
        {
            await Task.Delay(150);
            if (smokeDirectory != null)
            {
                using var image = new Bitmap(window.Width, window.Height); window.DrawToBitmap(image, new(0, 0, window.Width, window.Height));
                image.Save(Path.Combine(smokeDirectory, capture + (automatedResponse.Value ? "-confirm.png" : "-cancel.png")));
            }
            (automatedResponse.Value ? confirm : cancel).PerformClick();
        };
        return window.ShowDialog(owner) == DialogResult.OK;
    }
}
