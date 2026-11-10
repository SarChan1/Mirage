using System.Security.Cryptography;
using Mirage;

internal static class SaveFinderTests
{
    public static async Task RunAsync()
    {
        int passed = 0;
        void Check(bool condition, string description) { if (!condition) throw new Exception("FAIL " + description); Console.WriteLine("PASS " + description); passed++; }
        string testRoot = Path.Combine(Path.GetTempPath(), "Mirage.Tests.Saves." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        string Folder(string name) { string path = Path.Combine(testRoot, name); Directory.CreateDirectory(path); return path; }
        try {
            string roaming = Folder("Roaming"), documents = Folder("RedirectedDocuments"), install = Folder("Game"), savedGames = Folder("RedirectedSavedGames");
            string save = Path.Combine(Folder("Roaming\\Example\\12345"), "slot.sav");
            File.WriteAllText(save, "test save - do not modify");
            string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(save)));
            string manualSave = Path.Combine(install, "saves"); Directory.CreateDirectory(manualSave);
            string redirectedSave = Path.Combine(savedGames, "Example"); Directory.CreateDirectory(redirectedSave);
            string docSave = Path.Combine(documents, "Example"); Directory.CreateDirectory(docSave);
            var rule = new SaveGameRules { Name = "Example Game", Aliases = ["Other Name"], SteamIds = ["123"], Paths = ["<winAppData>/Example/<storeUserId>/*.sav", "<winDocuments>/Example", "<home>/Saved Games/Example", "<base>/saves", "<unsupported>/folder", "<base>/{a,b}"], Registry = ["HKEY_CURRENT_USER/Software/Example"] };
            var catalog = new SaveCatalog { SchemaVersion = 1, Games = [rule], UpdatedAt = "2026-11-02T00:00:00Z" };
            var game = new Game { Id = "123456789012345678", Name = "Different Store Title", SteamId = "123" };
            var environment = new SaveEnvironment { Folders = new() { ["<winAppData>"] = roaming, ["<winDocuments>"] = documents, ["<home>\\Saved Games"] = savedGames, ["<home>"] = testRoot }, RegistryExists = path => path.Contains("Example") };
            Check(SaveFinder.Match(catalog, game) == rule, "Steam identifier takes precedence over a different display name");
            Check(SaveFinder.Match(catalog, new Game { Name = "OTHER NAME" }) == rule, "exact aliases match case-insensitively");
            Check(SaveFinder.Match(new SaveCatalog { Games = [rule, new() { Name = "Example Game" }] }, new Game { Name = "Example Game" }) is null, "ambiguous names never select an arbitrary game");
            Check(SaveEnvironment.VdfValue("\"installdir\" \"My Game\"", "installdir") == "My Game", "Steam manifest directory is parsed");
            var finder = new SaveFinder(catalog, environment);
            var automatic = await finder.FindAsync(game);
            Check(automatic.CatalogName == rule.Name && automatic.CatalogCount == 1, "catalog match and provenance are returned");
            Check(automatic.Locations.Any(location => location.Path == save && location.Kind == "file"), "wildcard store profiles resolve to an existing save file");
            Check(automatic.Locations.Any(location => location.Path == docSave), "redirected Documents location is respected");
            Check(automatic.Locations.Any(location => location.Path == redirectedSave), "redirected Saved Games location is respected");
            Check(!automatic.InstallDetected && automatic.Locations.Length > 0, "existing saves remain discoverable without a detected installation");
            Check(automatic.RulesSkipped >= 2 && !automatic.Limited, "unsupported or installation-dependent rules are reported honestly");
            Check(automatic.Locations.Any(location => location.Kind == "registry"), "existing registry save locations are reported without reading values");
            Check(finder.Resolve(automatic.Locations.First(location => location.Kind == "file").Id).Path == save, "opening uses an opaque token issued by the last search");
            Check(finder.PathToCopy(automatic.Locations.First(location => location.Kind == "file").Id) == save, "copy resolves the precise file path without accessing the clipboard");
            Check(finder.PathToCopy(automatic.Locations.First(location => location.Kind == "registry").Id).Contains("HKEY_CURRENT_USER"), "registry save paths can be copied without executing registry tools");
            try { finder.PathToCopy(save); Check(false, "copy does not accept arbitrary paths"); } catch (ArgumentException) { Check(true, "copy does not accept arbitrary paths"); }
            try { finder.Resolve(save); Check(false, "arbitrary paths cannot be opened"); } catch (ArgumentException) { Check(true, "arbitrary paths cannot be opened"); }
            try { finder.Resolve(automatic.Locations.First(location => location.Kind == "registry").Id); Check(false, "registry paths are not executable or browsable files"); } catch (ArgumentException) { Check(true, "registry paths are not executable or browsable files"); }
            string oldToken = automatic.Locations.First(location => location.Kind == "file").Id;
            var manual = await finder.FindAsync(game, install);
            Check(manual.InstallDetected && manual.InstallFolder == install && manual.Locations.Any(location => location.Path == manualSave && location.Basis == "catalog"), "manual installation folder expands mapped base paths");
            Check(manual.Locations.Where(location => location.Kind != "registry").All(location => location.LastModified is not null), "save locations include their latest modification date");
            Check(manual.Locations.Any(location => location.FileCount > 0 && location.SizeBytes > 0), "save locations include bounded file counts and sizes");
            Check(manual.Locations.Select(location => location.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == manual.Locations.Length, "overlapping catalog and candidate matches are deduplicated");
            try { finder.Resolve(oldToken); Check(false, "old search tokens expire"); } catch (ArgumentException) { Check(true, "old search tokens expire"); }
            try { finder.PathToCopy(oldToken); Check(false, "copy tokens expire after another search"); } catch (ArgumentException) { Check(true, "copy tokens expire after another search"); }
            var unknown = await finder.FindAsync(new Game { Name = "Not In Catalog" }, install);
            Check(unknown.CatalogName is null && unknown.Locations.Any(location => location.Path == manualSave && location.Basis == "candidate"), "unmapped and non-store installations receive labelled local candidates");
            var empty = await finder.FindAsync(new Game { Name = "Missing Game" });
            Check(empty.Locations.Length == 0 && empty.CatalogName is null, "missing locations are not fabricated");
            foreach (string invalid in new[] { "relative", @"\\server\share", Path.Combine(testRoot, "Missing") }) {
                try { SaveFinder.ValidateFolder(invalid); Check(false, "invalid manual folder"); } catch (ArgumentException) { Check(true, "relative, remote or missing folders are rejected"); }
            }
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(save))) == originalHash, "search leaves save contents unchanged");
            string orphanFolder = Folder(@"Roaming\Orphan Game"); File.WriteAllText(Path.Combine(orphanFolder, "orphan.sav"), "preserved after uninstall");
            var orphanRule = new SaveGameRules { Name = "Orphan Game", Paths = ["<winAppData>/Orphan Game"] };
            var orphanGame = new Game { Id = "223456789012345678", Name = "Orphan Game" };
            var libraryFinder = new SaveFinder(new SaveCatalog { SchemaVersion = 1, Games = [rule, orphanRule] }, environment);
            var orphanScan = await libraryFinder.FindOrphansAsync([game, orphanGame], new HashSet<string> { game.Id });
            Check(orphanScan.Matches.Length == 1 && orphanScan.Matches[0].Game.Id == orphanGame.Id, "save center separates preserved saves from installed games");
            var library = new SaveLibraryService(libraryFinder, _ => new Dictionary<string, string[]> { [game.Id] = [install] });
            var dashboard = await library.ScanAsync([game, orphanGame]);
            Check(dashboard.InstalledCount == 1 && dashboard.OrphanCount == 1 && dashboard.WithSavesCount == 2 && dashboard.FileCount > 0, "save center summarizes installed and orphaned saves separately");
            string exportRoot = Folder("Exports");
            var folderExport = await library.ExportAsync([game, orphanGame], exportRoot, false);
            Check(folderExport.Selected && Directory.Exists(folderExport.Path) && File.Exists(Path.Combine(folderExport.Path!, "manifest.json")), "organized folder backup includes a manifest");
            var zipExport = await library.ExportAsync([game, orphanGame], exportRoot, true);
            Check(zipExport.Selected && File.Exists(zipExport.Path) && new FileInfo(zipExport.Path!).Length > 0, "zip backup is created as a separate portable archive");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(save))) == originalHash, "backup leaves original save contents unchanged");
            string deep = Folder("Deep"); for (int i = 0; i < 11; i++) { deep = Path.Combine(deep, "level"); Directory.CreateDirectory(deep); }
            var limitedRule = new SaveGameRules { Name = "Deep", Paths = ["<home>/Deep/**/never.sav"] };
            var limited = await new SaveFinder(new SaveCatalog { SchemaVersion = 1, Games = [limitedRule] }, environment).FindAsync(new Game { Name = "Deep" });
            Check(limited.Limited, "recursive searches stop at the depth limit");
            var bundled = await new SaveFinder(environment: environment).FindAsync(new Game { Name = "Cyberpunk 2077", SteamId = "1091500" });
            Check(bundled.CatalogCount > 10000 && bundled.CatalogName == "Cyberpunk 2077", "bundled public catalog works offline and matches Cyberpunk by Steam id");
            Console.WriteLine($"SAVE_TESTS_OK {passed}");
        } finally {
            string full = Path.GetFullPath(testRoot);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("Mirage.Tests.Saves.")) Directory.Delete(full, true);
        }
    }
}
