using System.Text.Json;
using Mirage;

if (args.Contains("--controller")) { await ControllerTests.RunAsync(); return; }
if (args.Contains("--saves")) { await SaveFinderTests.RunAsync(); return; }
if (args.Contains("--preferences")) { PreferencesTests.Run(); return; }

if (args.Length == 2 && args[0] == "--orphan-parent") {
    var parentService = new ProcessSession();
    var game = new Game { Id = "123456789012345678", Name = "Orphan test", Executables = [new() { Name = "mirage-orphan-test.exe", Os = "win32" }] };
    await parentService.StartAsync(game, game.Executables[0], Path.Combine(Path.GetTempPath(), "Mirage.Tests"));
    File.WriteAllText(args[1], parentService.ProcessId!.Value.ToString());
    Environment.Exit(0);
}

if (args.Contains("--refresh")) {
    var refreshed = await new CatalogService().RefreshAsync();
    if (!refreshed.Any(g => g.Id == "787443973538971748")) throw new Exception("Cyberpunk missing from refreshed catalog");
    Console.WriteLine("LIVE_CATALOG_REFRESH_OK " + refreshed.Count);
    return;
}

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS " + name); passed++; }
if (args.Contains("--library")) {
    Game GameItem(string id, string name, bool supported = true) => new() { Id = id, Name = name, Executables = supported ? [new() {Name="game.exe",Os="win32"},new() {Name="bin/alternative.exe",Os="win32"}] : [] };
    var alpha = GameItem("123456789012345678", "_Alpha");
    var tree = GameItem("223456789012345678", "Árvore");
    var zelda = GameItem("323456789012345678", "Zelda");
    var number = GameItem("423456789012345678", "123 Game");
    var unsupported = GameItem("523456789012345678", "Unsupported process", false);
    var library = new[] { zelda, tree, alpha, number, unsupported };
    Check(LibraryTools.Sort(library, "default").Select(game => game.Id).SequenceEqual(library.Select(game => game.Id)), "default ordering preserves ranked search and recent history");
    Check(LibraryTools.Sort(library, "nameAsc").Select(game => game.Id).SequenceEqual(new[] {number.Id,alpha.Id,tree.Id,unsupported.Id,zelda.Id}), "alphabetical order handles accents and leading punctuation");
    Check(LibraryTools.Sort(library, "nameDesc").First().Id == zelda.Id && LibraryTools.Sort(library, "nameDesc").Last().Id == number.Id, "descending order reverses the name direction");
    try { LibraryTools.Sort(library,"arbitrary"); Check(false,"reject invalid order"); } catch (ArgumentException) { Check(true,"invalid ordering is rejected"); }
    var additions = LibraryTools.PrepareAddition(library, new[] {tree.Id,alpha.Id}, 2, 18, false, alpha.Id, 1);
    Check(additions.Select(item => item.GameId).SequenceEqual(new[] {tree.Id,alpha.Id}), "bulk addition preserves user selection order");
    Check(additions.All(item => item.DurationMinutes == 18 && !item.ShowWindow && item.IsValid), "bulk addition preserves time and window preferences");
    Check(additions[1].Executable == "bin/alternative.exe" && additions[0].Executable == "game.exe", "selected game keeps its chosen variant and others use the first compatible executable");
    var again = LibraryTools.PrepareAddition(library,new[] {tree.Id,alpha.Id},0,16,true);
    Check(additions.Concat(again).Select(item => item.EntryId).Distinct().Count() == 4, "each bulk operation creates independent entry identities");
    foreach (var ids in new[] { Array.Empty<string>(), new[] {alpha.Id,alpha.Id}, new[] {alpha.Id,"999999999999999999"}, new[] {alpha.Id,unsupported.Id}, new[] {"bad"}, Enumerable.Range(0,101).Select(i => (100000000000000000L+i).ToString()).ToArray() }) {
        try { LibraryTools.PrepareAddition(library,ids,0,16,true); Check(false,"reject invalid selection"); } catch (ArgumentException) { Check(true,"invalid or incompatible selection is rejected"); }
    }
    foreach (int minutes in new[] {0,-1,1441}) {
        try { LibraryTools.PrepareAddition(library,new[] {alpha.Id},0,minutes,true); Check(false,"reject invalid bulk time"); } catch (ArgumentException) { Check(true,"invalid bulk time is rejected"); }
    }
    try { LibraryTools.PrepareAddition(library,new[] {alpha.Id,tree.Id},499,16,true); Check(false,"reject queue overflow"); } catch (InvalidOperationException) { Check(true,"selection cannot overflow the current queue"); }
    GameSearch.Prepare(library);
    var cachedOptions = ProcessSession.Options(alpha);
    Check(ReferenceEquals(cachedOptions, ProcessSession.Options(alpha)), "prepared catalog reuses validated executable choices");
    alpha.Executables = [new() { Name = "replacement.exe", Os = "win32" }];
    GameSearch.Prepare(library);
    Check(ProcessSession.Options(alpha).Single().Name == "replacement.exe", "catalog preparation invalidates stale executable choices");
    Check(GameSearch.Find(library, "arvore").Single().Id == tree.Id, "cached catalog keeps accent-insensitive search behavior");
    Console.WriteLine($"{passed} library checks passed.");
    return;
}
if (args.Contains("--workspace")) {
    ProcessPlaylistItem Item(string name, int minutes = 16) => new() { GameId = "123456789012345678", GameName = name, Executable = "game.exe", DurationMinutes = minutes };
    var preferences = new Preferences { ProcessPlaylist = [Item("First"), Item("Second", 30)] };
    var workspace = new PlaylistWorkspace(preferences);
    Check(!workspace.CanUndo, "new workspace starts without undo history");
    var saved = workspace.Save("  Evening  ");
    Check(saved.Name == "Evening" && saved.Items.Count == 2, "named template retains order and trims name");
    preferences.ProcessPlaylist[0].DurationMinutes = 90;
    Check(saved.Items[0].DurationMinutes == 16, "editing the current queue does not mutate saved templates");
    workspace.Use(saved.Id, false);
    var loadedIds = preferences.ProcessPlaylist.Select(item => item.EntryId).ToArray();
    Check(preferences.ProcessPlaylist[0].DurationMinutes == 16 && loadedIds.All(id => saved.Items.All(item => item.EntryId != id)), "loading a template restores settings with fresh entry identities");
    workspace.Use(saved.Id, true);
    Check(preferences.ProcessPlaylist.Count == 4 && preferences.ProcessPlaylist.Select(item => item.EntryId).Distinct().Count() == 4, "appending a template preserves duplicates as independent entries");
    workspace.Undo();
    Check(preferences.ProcessPlaylist.Count == 2 && preferences.ProcessPlaylist[0].EntryId == loadedIds[0], "undo restores the exact queue before append");
    workspace.SetAllDurations(21);
    Check(preferences.ProcessPlaylist.All(item => item.DurationMinutes == 21) && saved.Items[1].DurationMinutes == 30, "bulk duration affects only the current queue");
    workspace.Undo();
    Check(preferences.ProcessPlaylist.Select(item => item.DurationMinutes).SequenceEqual(new[] {16,30}), "undo bulk duration restores individual times");
    foreach (string name in new[] { "", "evening", new string('a', 61), "bad\nname" }) {
        try { workspace.Save(name); Check(false, "reject invalid saved name"); } catch (ArgumentException) { Check(true, "invalid or duplicate template name is rejected"); }
    }
    workspace.Rename(saved.Id, "Weekend");
    Check(saved.Name == "Weekend", "renaming updates the selected saved sequence");
    var restoredPreferences = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
    Check(restoredPreferences.SavedPlaylists.Count == 1 && restoredPreferences.SavedPlaylists[0].Name == "Weekend" && restoredPreferences.SavedPlaylists[0].IsValid, "saved library survives a preferences serialization round trip");
    var legacyPreferences = JsonSerializer.Deserialize<Preferences>("{\"PlaylistDefaultMinutes\":16}")!;
    Check(legacyPreferences.SavedPlaylists.Count == 0, "older preferences acquire an empty library without migration losses");
    int beforeInvalid = preferences.ProcessPlaylist.Count;
    try { workspace.SetAllDurations(0); Check(false, "reject invalid bulk duration"); } catch (ArgumentException) { Check(preferences.ProcessPlaylist.Count == beforeInvalid && preferences.ProcessPlaylist[0].DurationMinutes == 16, "invalid bulk edits leave the queue intact"); }
    workspace.Delete(saved.Id);
    Check(preferences.SavedPlaylists.Count == 0 && preferences.ProcessPlaylist.Count == beforeInvalid, "deleting a template preserves the loaded queue");
    workspace.ClearHistory();
    for (int i = 0; i < 25; i++) workspace.Remember("Edit");
    int undos = 0;
    while (workspace.CanUndo) { workspace.Undo(); undos++; }
    Check(undos == 20, "undo history has a bounded twenty-snapshot memory cost");
    var nearlyFull = new Preferences { ProcessPlaylist = Enumerable.Range(0,500).Select(i => Item("Game " + i)).ToList() };
    var fullWorkspace = new PlaylistWorkspace(nearlyFull); var fullTemplate = fullWorkspace.Save("Full");
    try { fullWorkspace.Use(fullTemplate.Id, true); Check(false, "reject excessive append"); } catch (InvalidOperationException) { Check(nearlyFull.ProcessPlaylist.Count == 500 && !fullWorkspace.CanUndo, "oversized append is rejected before mutation or history changes"); }
    Console.WriteLine($"{passed} workspace checks passed.");
    return;
}
if (args.Contains("--ui-models")) {
    Check(new Preferences().PlaylistDefaultMinutes == 16 && new ProcessPlaylistItem().DurationMinutes == 16, "new default duration is 16 minutes");
    var legacy = new Preferences { PlaylistDefaultMinutes = 15, ProcessPlaylist = [new() { DurationMinutes = 15 }] };
    legacy.NormalizePlaylistDefaults();
    Check(legacy.PlaylistDefaultMinutes == 16 && legacy.ProcessPlaylist[0].DurationMinutes == 15, "legacy default migrates without changing saved entries");
    legacy.PlaylistDefaultMinutes = 15; legacy.NormalizePlaylistDefaults();
    Check(legacy.PlaylistDefaultMinutes == 15, "migration runs only once and preserves later choices");
    var custom = new Preferences { PlaylistDefaultMinutes = 30 }; custom.NormalizePlaylistDefaults();
    Check(custom.PlaylistDefaultMinutes == 30, "custom duration is preserved");
    var compatible = new Game { Executables = [new() { Name = "game.exe", Os = "win32" }] };
    Check(compatible.SupportsPlaylist, "compatible Windows game supports a sequence");
    Check(!new Game().SupportsPlaylist, "games without a compatible Windows executable are unavailable");
    Console.WriteLine($"{passed} UI model checks passed.");
    return;
}
var catalog = await new CatalogService().LoadAsync();
Check(catalog.Count >= 25000, "bundled catalog has at least 25,000 games");
var cyber = GameSearch.Find(catalog, "cyberpunk").First();
Check(cyber.Id == "787443973538971748", "Cyberpunk resolves to the actual Discord application ID");
Check(GameSearch.Find(catalog, "rEd DeAd").Any(g => g.Name == "Red Dead Redemption 2"), "case-insensitive multiword search");
var alias = new Game { Id = "123456789012345678", Name = "Árvore do Saber", Aliases = ["Tree of Wisdom"] }; GameSearch.Prepare([alias]);
Check(GameSearch.Find([alias], "arvore").Count() == 1 && GameSearch.Find([alias], "wisdom").Count() == 1, "accent-insensitive names and aliases");
Check(!GameSearch.Find(catalog, "zzzzNoGameLikeThis12345").Any(), "empty search result");
var playlistItems = new[] {
    new ProcessPlaylistItem { GameId = "123456789012345678", GameName = "First", Executable = "first.exe", DurationMinutes = 15 },
    new ProcessPlaylistItem { GameId = "223456789012345678", GameName = "Second", Executable = "second.exe", DurationMinutes = 30, ShowWindow = false }
};
var playlist = new ProcessPlaylist(playlistItems);
var playlistStart = DateTimeOffset.FromUnixTimeSeconds(1700000000);
Check(playlist.Start(playlistStart).GameName == "First" && playlist.Index == 0 && playlist.EndsAt == playlistStart.AddMinutes(15), "playlist starts with the first game and its own duration");
Check(!playlist.IsDue(playlistStart.AddMinutes(14)) && playlist.IsDue(playlistStart.AddMinutes(15)), "playlist changes only when the current duration ends");
Check(playlist.Remaining(playlistStart.AddMinutes(10)) == TimeSpan.FromMinutes(5), "playlist reports the current remaining time");
Check(playlist.Advance(playlistStart.AddMinutes(15))?.GameName == "Second" && playlist.EndsAt == playlistStart.AddMinutes(45), "playlist advances and applies the next duration");
Check(playlist.Advance(playlistStart.AddMinutes(45)) is null && playlist.Completed, "playlist finishes after its final game");
var snapshotPlan = new ProcessPlaylist(playlistItems);
playlistItems[0].GameName = "Changed outside";
Check(snapshotPlan.Start(playlistStart).GameName == "First", "playlist keeps an internal snapshot of entries");
try { _ = new ProcessPlaylist([new ProcessPlaylistItem { GameId = "123456789012345678", GameName = "Invalid", Executable = "bad.exe", DurationMinutes = 0 }]); Check(false, "invalid playlist duration"); } catch (ArgumentException) { Check(true, "playlist rejects invalid durations"); }

Check(catalog.Count(g => ProcessSession.Options(g).Count > 0) > 10000, "bundled catalog contains more than 10,000 process-compatible games");
var resident = catalog.First(g => g.Id == "1129504209226711190");
Check(ProcessSession.Options(resident).Any(e => e.Name == "resident evil 4  biohazard re4/re4.exe"), "Resident Evil 4 keeps exact directory spacing and executable");
var choices = new Game { Id = "123456789012345678", Name = "Variants", Executables = [new() { Name = "game/a.exe", Os = "win32" }, new() { Name = "game/A.exe", Os = "win32" }, new() { Name = "launcher.exe", Os = "win32", IsLauncher = true }, new() { Name = "game/b.exe", Os = "win32" }, new() { Name = "game", Os = "linux" }] };
Check(ProcessSession.Options(choices).Count == 2, "Windows variants omit launchers, other operating systems and duplicates");
foreach (string invalidPath in new[] { "../outside.exe", "a/../../outside.exe", "C:/Windows/test.exe", @"\\server\share\test.exe", "a/test.exe:stream", "NUL.exe", "a/CON/test.exe", "a./test.exe", "game.txt", "/root.exe" }) {
    try { ProcessSession.ValidateRelativePath(invalidPath); Check(false, "reject " + invalidPath); } catch (ArgumentException) { Check(true, "reject unsafe path " + invalidPath); }
}
string testRoot = Path.Combine(Path.GetTempPath(), "Mirage.Tests", Guid.NewGuid().ToString("N"));
string resolved = ProcessSession.ResolvePath(testRoot, choices.Id, "0123456789ABCDEF", @"game\a.exe");
Check(resolved.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "resolved executable remains inside its own root");
try { ProcessSession.ResolvePath(testRoot, "../../outside", "0123456789ABCDEF", "a.exe"); Check(false, "invalid game ID"); } catch (ArgumentException) { Check(true, "game IDs cannot redirect writes"); }
using (var processes = new ProcessSession()) {
    await processes.StartAsync(resident, ProcessSession.Options(resident)[0], testRoot);
    int workerId = processes.ProcessId!.Value;
    using var worker = System.Diagnostics.Process.GetProcessById(workerId);
    Check(processes.Running && worker.ProcessName.Equals("re4", StringComparison.OrdinalIgnoreCase), "real Windows process uses re4.exe as its image name");
    Check(worker.MainModule!.FileName == processes.ExecutablePath && processes.ExecutablePath!.EndsWith(@"resident evil 4  biohazard re4\re4.exe"), "actual process image preserves catalog path suffix");
    Check(new FileInfo(processes.ExecutablePath!).Length < 16384, "process helper is smaller than 16 KB");
    await processes.StartAsync(choices, ProcessSession.Options(choices)[1], testRoot, showWindow: true);
    Check(worker.HasExited && processes.Running, "switching games closes the previous worker first");
    using (var windowWorker = System.Diagnostics.Process.GetProcessById(processes.ProcessId!.Value)) {
        for (int attempt = 0; attempt < 20 && windowWorker.MainWindowHandle == IntPtr.Zero; attempt++) { await Task.Delay(100); windowWorker.Refresh(); }
        Check(windowWorker.MainWindowHandle != IntPtr.Zero && windowWorker.MainWindowTitle.Contains("Mirage"), "optional worker window has a real Windows handle");
    }
    await processes.StopAsync();
    Check(!processes.Running && processes.ProcessId is null, "stop ends the worker and clears session state");
}
using (var processes = new ProcessSession()) {
    await processes.StartAsync(choices, ProcessSession.Options(choices)[0], testRoot);
    string path = processes.ExecutablePath!; await processes.StopAsync();
    File.WriteAllBytes(path, [0, 1, 2]);
    try { await processes.StartAsync(choices, ProcessSession.Options(choices)[0], testRoot); Check(false, "tampered helper"); } catch (IOException) { Check(!processes.Running, "altered local helpers are rejected"); }
}
string report = Path.Combine(testRoot, "orphan-pid.txt");
var parentInfo = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
parentInfo.ArgumentList.Add("--orphan-parent"); parentInfo.ArgumentList.Add(report);
using (var parent = System.Diagnostics.Process.Start(parentInfo)!) {
    await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    int orphanId = int.Parse(await File.ReadAllTextAsync(report));
    bool exited = false;
    for (int attempt = 0; attempt < 30; attempt++) {
        try { using var orphan = System.Diagnostics.Process.GetProcessById(orphanId); if (orphan.HasExited) { exited = true; break; } } catch (ArgumentException) { exited = true; break; }
        await Task.Delay(100);
    }
    Check(exited, "worker exits by itself when its parent app disappears");
}

Console.WriteLine($"\n{passed} checks passed. No real Discord client was contacted.");
