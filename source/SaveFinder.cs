using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Mirage;

public sealed class SaveGameRules
{
    public string Name { get; set; } = "";
    public string[] Aliases { get; set; } = [];
    public string[] SteamIds { get; set; } = [];
    public string[] InstallDirs { get; set; } = [];
    public string[] Paths { get; set; } = [];
    public string[] Registry { get; set; } = [];
}
public sealed class SaveCatalog
{
    public int SchemaVersion { get; set; }
    public string UpdatedAt { get; set; } = "";
    public SaveGameRules[] Games { get; set; } = [];
}
public sealed record SaveLocation(string Id, string Path, string Kind, string Basis = "catalog", DateTimeOffset? LastModified = null, long SizeBytes = 0, int FileCount = 0);
public sealed record SaveSearchResult(string GameName, string? CatalogName, int CatalogCount, string CatalogUpdatedAt, bool InstallDetected, string? InstallFolder, SaveLocation[] Locations, int RulesChecked, int RulesSkipped, bool Limited);
public sealed record OrphanSaveMatch(Game Game, SaveSearchResult Search);
public sealed record OrphanSaveScan(OrphanSaveMatch[] Matches, bool Limited);

// Metadata-only, bounded local search: never reads, changes or uploads save contents.
public sealed class SaveFinder
{
    private static readonly object CatalogLock = new();
    private static WeakReference<SaveCatalog>? bundled;
    private static SaveCatalog LoadCatalog()
    {
        lock (CatalogLock) {
        if (bundled is not null && bundled.TryGetTarget(out var cached)) return cached;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mirage.catalog.saves.json") ?? throw new IOException("Catálogo de saves indisponível.");
        var catalog = JsonSerializer.Deserialize<SaveCatalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new IOException("Catálogo de saves inválido.");
        if (catalog.SchemaVersion != 1) throw new IOException("Versão do catálogo de saves inválida.");
        bundled = new WeakReference<SaveCatalog>(catalog);
        return catalog;
        }
    }
    private readonly SaveCatalog? catalogOverride;
    private readonly SaveEnvironment? environmentOverride;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<string, SaveLocation> lastLocations = [];
    public SaveFinder(SaveCatalog? catalog = null, SaveEnvironment? environment = null) { catalogOverride = catalog; environmentOverride = environment; }
    private static string Key(string name) => new(GameSearch.Normalize(name).Where(char.IsLetterOrDigit).ToArray());
    public static SaveGameRules? Match(SaveCatalog catalog, Game game)
    {
        var bySteam = string.IsNullOrEmpty(game.SteamId) ? [] : catalog.Games.Where(rule => rule.SteamIds.Contains(game.SteamId)).ToArray();
        if (bySteam.Length == 1) return bySteam[0];
        var names = new[] { game.Name }.Concat(game.Aliases).Select(Key).Where(key => key.Length > 0).ToHashSet();
        var exact = catalog.Games.Where(rule => new[] { rule.Name }.Concat(rule.Aliases).Any(name => names.Contains(Key(name)))).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }
    public async Task<SaveSearchResult> FindAsync(Game game, string? manualFolder = null)
    {
        await gate.WaitAsync();
        try {
            var result = await Task.Run(() => Search(game, manualFolder));
            lastLocations = result.Locations.ToDictionary(location => location.Id);
            return result;
        } finally { gate.Release(); }
    }
    public async Task<OrphanSaveScan> FindOrphansAsync(IEnumerable<Game> source, ISet<string> installedIds)
    {
        await gate.WaitAsync();
        try { return await Task.Run(() => ScanOrphans(source, installedIds)); }
        finally { gate.Release(); }
    }
    public async Task<SaveLocation[]> RefreshKnownAsync(IEnumerable<SaveLocation> known)
    {
        await gate.WaitAsync();
        try {
            var result = await Task.Run(() => known
                .Where(location => location.Kind != "registry" && (File.Exists(location.Path) || Directory.Exists(location.Path)))
                .Take(160)
                .Select(Describe)
                .ToArray());
            lastLocations = result.ToDictionary(location => location.Id);
            return result;
        } finally { gate.Release(); }
    }
    public SaveLocation Resolve(string id) => lastLocations.TryGetValue(id, out var result) && result.Kind != "registry" && (Directory.Exists(result.Path) || File.Exists(result.Path)) ? result : throw new ArgumentException("Esse local não está mais disponível. Faça uma nova busca.");
    public string PathToCopy(string id) => lastLocations.TryGetValue(id, out var result) ? result.Path : throw new ArgumentException("Faça uma nova busca antes de copiar esse caminho.");
    public static string ValidateFolder(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length > 500 || !Directory.Exists(path)) throw new ArgumentException("Selecione uma pasta existente em uma unidade local.");
        return System.IO.Path.GetFullPath(path);
    }
    private SaveSearchResult Search(Game game, string? manualFolder, string[]? knownInstalls = null, SaveGameRules? knownRule = null, SaveEnvironment? knownEnvironment = null)
    {
        var catalog = catalogOverride ?? LoadCatalog();
        var rule = knownRule ?? Match(catalog, game);
        var environment = knownEnvironment ?? environmentOverride ?? SaveEnvironment.Current();
        var installs = manualFolder is not null ? new[] { ValidateFolder(manualFolder) } : knownInstalls ?? environment.Installs(game, rule);
        var locations = new Dictionary<string, SaveLocation>(StringComparer.OrdinalIgnoreCase);
        var budget = new SearchBudget(); int checkedRules = 0, skipped = 0;
        if (rule is not null) {
            foreach (var pattern in rule.Paths) {
                if (budget.Limited || locations.Count >= 80) { budget.Limited = true; break; }
                var expanded = Expand(pattern, rule, environment, installs).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (expanded.Length == 0) { skipped++; continue; }
                checkedRules++;
                foreach (var path in expanded) {
                    foreach (var found in Glob(path, budget)) {
                        string full = System.IO.Path.GetFullPath(found);
                        if (locations.Count >= 80) { budget.Limited = true; break; }
                        locations.TryAdd(full, new SaveLocation(Guid.NewGuid().ToString("N"), full, Directory.Exists(full) ? "folder" : "file"));
                    }
                }
            }
            foreach (var registryPath in rule.Registry.Take(32)) {
                if (environment.RegistryExists(registryPath)) locations.TryAdd(registryPath, new SaveLocation(Guid.NewGuid().ToString("N"), registryPath, "registry"));
            }
        }
        // Non-store installations may keep saves beside the game or in another profile.
        // These are explicitly labelled candidates, not catalog-confirmed locations.
        var candidates = installs.SelectMany(install => new[] { "save", "saves", "savegame", "savegames", "*.sav", "*.save" }.Select(part => System.IO.Path.Combine(install, part)));
        if (!string.IsNullOrEmpty(game.SteamId) && game.SteamId.All(char.IsAsciiDigit)) {
            var generic = new SaveGameRules { SteamIds = [game.SteamId] };
            candidates = candidates.Concat(Expand("<winPublic>/Documents/Steam/*/<storeGameId>", generic, environment, installs));
            candidates = candidates.Concat(Expand("<winAppData>/*Steam*/<storeGameId>", generic, environment, installs));
        }
        foreach (var pattern in candidates) {
            if (budget.Limited || locations.Count >= 80) { budget.Limited = true; break; }
            foreach (var found in Glob(pattern, budget)) {
                string full = System.IO.Path.GetFullPath(found);
                if (locations.Count >= 80) { budget.Limited = true; break; }
                locations.TryAdd(full, new SaveLocation(Guid.NewGuid().ToString("N"), full, Directory.Exists(full) ? "folder" : "file", "candidate"));
            }
        }
        return new(game.Name, rule?.Name, catalog.Games.Length, catalog.UpdatedAt, installs.Length > 0, installs.FirstOrDefault(), locations.Values.Select(Describe).ToArray(), checkedRules, skipped, budget.Limited);
    }
    private OrphanSaveScan ScanOrphans(IEnumerable<Game> source, ISet<string> installedIds)
    {
        var catalog = catalogOverride ?? LoadCatalog();
        var games = source.ToArray();
        var bySteam = games.Where(game => !string.IsNullOrEmpty(game.SteamId)).GroupBy(game => game.SteamId!).ToDictionary(group => group.Key, group => group.DistinctBy(game => game.Id).ToArray());
        var byName = games.SelectMany(game => new[] { game.Name }.Concat(game.Aliases ?? []).Select(name => (Key: Key(name), Game: game))).Where(item => item.Key.Length > 0).GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.Select(item => item.Game).DistinctBy(game => game.Id).ToArray());
        var environment = environmentOverride ?? SaveEnvironment.Current();
        var watch = Stopwatch.StartNew(); var matches = new List<OrphanSaveMatch>(); bool limited = false;
        foreach (var rule in catalog.Games.Where(rule => rule.Paths.Length > 0)) {
            if (watch.Elapsed > TimeSpan.FromSeconds(45) || matches.Count >= 80) { limited = true; break; }
            Game? game = null;
            var steamMatches = rule.SteamIds.SelectMany(id => bySteam.GetValueOrDefault(id) ?? []).DistinctBy(item => item.Id).ToArray();
            if (steamMatches.Length == 1) game = steamMatches[0];
            if (game is null) {
                var nameMatches = new[] { rule.Name }.Concat(rule.Aliases).SelectMany(name => byName.GetValueOrDefault(Key(name)) ?? []).DistinctBy(item => item.Id).ToArray();
                if (nameMatches.Length == 1) game = nameMatches[0];
            }
            if (game is null || installedIds.Contains(game.Id) || matches.Any(item => item.Game.Id == game.Id)) continue;
            var result = Search(game, null, [], rule, environment);
            if (result.Locations.Any(location => location.Kind != "registry")) matches.Add(new(game, result));
        }
        return new(matches.ToArray(), limited);
    }
    private static SaveLocation Describe(SaveLocation location)
    {
        if (location.Kind == "registry") return location;
        try {
            if (File.Exists(location.Path)) {
                var file = new FileInfo(location.Path);
                return location with { LastModified = file.LastWriteTimeUtc, SizeBytes = file.Length, FileCount = 1 };
            }
            if (!Directory.Exists(location.Path)) return location;
            long bytes = 0; int count = 0; DateTime latest = Directory.GetLastWriteTimeUtc(location.Path);
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true, MaxRecursionDepth = 16 };
            foreach (string path in Directory.EnumerateFiles(location.Path, "*", options).Take(20000)) {
                try { var file = new FileInfo(path); bytes += file.Length; count++; if (file.LastWriteTimeUtc > latest) latest = file.LastWriteTimeUtc; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return location with { LastModified = latest, SizeBytes = bytes, FileCount = count };
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return location; }
    }
    private static IEnumerable<string> Expand(string pattern, SaveGameRules rule, SaveEnvironment environment, string[] installs)
    {
        string path = pattern.Replace('/', '\\');
        foreach (var (token, value) in environment.Folders) path = path.Replace(token, value, StringComparison.Ordinal);
        path = path.Replace("<storeUserId>", "*").Replace("<osUserName>", Environment.UserName);
        IEnumerable<string> paths = [path];
        if (path.Contains("<base>")) paths = installs.Select(install => path.Replace("<base>", install));
        else if (path.Contains("<root>")) paths = environment.SteamRoots.Select(root => path.Replace("<root>", root));
        paths = paths.SelectMany(candidate => candidate.Contains("<game>") ? rule.InstallDirs.Select(dir => candidate.Replace("<game>", dir)) : [candidate]);
        paths = paths.SelectMany(candidate => candidate.Contains("<storeGameId>") ? rule.SteamIds.Select(id => candidate.Replace("<storeGameId>", id)) : [candidate]);
        return paths.Where(candidate => !candidate.Contains('<') && !candidate.Contains('>') && !candidate.Contains('{') && !candidate.Contains('[') && System.IO.Path.IsPathFullyQualified(candidate) && !candidate.StartsWith(@"\\", StringComparison.Ordinal));
    }
    private sealed class SearchBudget
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private int entries;
        public bool Limited { get; set; }
        public bool Allow() { if (++entries > 20000 || watch.Elapsed > TimeSpan.FromSeconds(6)) Limited = true; return !Limited; }
    }
    private static IEnumerable<string> Glob(string pattern, SearchBudget budget)
    {
        string? root = System.IO.Path.GetPathRoot(pattern);
        if (string.IsNullOrEmpty(root)) yield break;
        string[] parts = pattern[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in Walk(root, 0, 0)) yield return path;
        IEnumerable<string> Walk(string current, int index, int depth)
        {
            if (!budget.Allow()) yield break;
            if (index == parts.Length) { if (Directory.Exists(current) || File.Exists(current)) yield return current; yield break; }
            if (!Directory.Exists(current) || IsLink(current)) yield break;
            string part = parts[index];
            if (part == "**") {
                foreach (var match in Walk(current, index + 1, depth)) yield return match;
                if (depth >= 8) { budget.Limited = true; yield break; }
                foreach (string child in Entries(current, true)) {
                    if (!budget.Allow()) yield break;
                    foreach (var match in Walk(child, index, depth + 1)) yield return match;
                }
            } else if (part.Contains('*') || part.Contains('?')) {
                foreach (string child in Entries(current, index < parts.Length - 1)) {
                    if (!budget.Allow()) yield break;
                    if (!FileSystemName.MatchesSimpleExpression(part, System.IO.Path.GetFileName(child), true)) continue;
                    foreach (var match in Walk(child, index + 1, depth)) yield return match;
                }
            } else {
                string next;
                try { next = System.IO.Path.GetFullPath(System.IO.Path.Combine(current, part)); } catch (ArgumentException) { yield break; }
                foreach (var match in Walk(next, index + 1, depth)) yield return match;
            }
        }
    }
    private static bool IsLink(string path) { try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return true; } }
    private static string[] Entries(string directory, bool foldersOnly)
    {
        try {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = false };
            return (foldersOnly ? Directory.EnumerateDirectories(directory, "*", options) : Directory.EnumerateFileSystemEntries(directory, "*", options)).Take(20001).ToArray();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

public sealed class SaveEnvironment
{
    public Dictionary<string, string> Folders { get; init; } = [];
    public string[] SteamRoots { get; init; } = [];
    public Func<Game, SaveGameRules?, string[]> Installs { get; init; } = (_, _) => [];
    public Func<string, bool> RegistryExists { get; init; } = _ => false;
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
    private static string SavedGames(string home)
    {
        IntPtr pointer = IntPtr.Zero;
        try { var id = new Guid("4C5C32FF-BB9D-43B0-BF4E-1CAAA3AF320F"); if (SHGetKnownFolderPath(in id, 0, IntPtr.Zero, out pointer) == 0) return Marshal.PtrToStringUni(pointer) ?? System.IO.Path.Combine(home, "Saved Games"); }
        finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
        return System.IO.Path.Combine(home, "Saved Games");
    }
    public static SaveEnvironment Current()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new Dictionary<string, string> {
            ["<home>/Saved Games"] = SavedGames(home),
            ["<home>\\Saved Games"] = SavedGames(home),
            ["<home>"] = home,
            ["<winDocuments>"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["<winAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["<winLocalAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["<winLocalAppDataLow>"] = System.IO.Path.Combine(home, "AppData", "LocalLow"),
            ["<winPublic>"] = Environment.GetEnvironmentVariable("PUBLIC") ?? System.IO.Path.Combine(System.IO.Path.GetPathRoot(home)!, "Users", "Public"),
            ["<winProgramData>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["<winDir>"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        string[] roots = SteamLibraries();
        return new() { Folders = folders, SteamRoots = roots, Installs = (game, rule) => FindInstalls(game, rule, roots), RegistryExists = RegistryKeyExists };
    }
    public static Dictionary<string, string[]> DetectInstalls(IEnumerable<Game> source)
    {
        var games = source.ToArray();
        var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var bySteam = games.Where(game => !string.IsNullOrEmpty(game.SteamId)).GroupBy(game => game.SteamId!).ToDictionary(group => group.Key, group => group.ToArray());
        var byName = games.SelectMany(game => new[] { game.Name }.Concat(game.Aliases ?? []).Select(name => (Key: GameSearch.Normalize(name.Trim()), Game: game)))
            .Where(item => item.Key.Length > 1).GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.Select(item => item.Game).DistinctBy(game => game.Id).ToArray());
        foreach (string root in SteamLibraries()) {
            string manifests = System.IO.Path.Combine(root, "steamapps");
            try {
                foreach (string file in Directory.EnumerateFiles(manifests, "appmanifest_*.acf").Take(5000)) {
                    string id = System.IO.Path.GetFileNameWithoutExtension(file)["appmanifest_".Length..];
                    if (!bySteam.TryGetValue(id, out var matches)) continue;
                    string dir = VdfValue(ReadSmall(file), "installdir");
                    if (dir.Length == 0 || dir != System.IO.Path.GetFileName(dir)) continue;
                    string path = System.IO.Path.Combine(root, "steamapps", "common", dir);
                    if (Directory.Exists(path)) foreach (var game in matches) Add(game, path);
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        string epic = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        try {
            if (Directory.Exists(epic)) foreach (string file in Directory.EnumerateFiles(epic, "*.item").Take(1000)) {
                try {
                    using var document = JsonDocument.Parse(ReadSmall(file));
                    string name = document.RootElement.TryGetProperty("DisplayName", out var title) ? title.GetString() ?? "" : "";
                    string path = document.RootElement.TryGetProperty("InstallLocation", out var location) ? location.GetString() ?? "" : "";
                    AddName(name, path);
                } catch (JsonException) { }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 }) {
            try {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (string key in uninstall.GetSubKeyNames().Take(2000)) {
                    using var entry = uninstall.OpenSubKey(key);
                    AddName(entry?.GetValue("DisplayName") as string ?? "", entry?.GetValue("InstallLocation") as string ?? "");
                }
            } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return found.ToDictionary(item => item.Key, item => item.Value.ToArray());
        void AddName(string name, string path) { string key = GameSearch.Normalize(name.Trim()); if (byName.TryGetValue(key, out var matches) && matches.Length == 1) Add(matches[0], path); }
        void Add(Game game, string path) {
            if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || !Directory.Exists(path)) return;
            if (!found.TryGetValue(game.Id, out var paths)) found[game.Id] = paths = new(StringComparer.OrdinalIgnoreCase);
            paths.Add(System.IO.Path.GetFullPath(path));
        }
    }
    internal static string VdfValue(string text, string key)
    {
        var match = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s*\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Replace(@"\\", @"\") : "";
    }
    private static string ReadSmall(string path)
    {
        try { return new FileInfo(path) is { Exists: true, Length: < 2_000_000 } ? File.ReadAllText(path) : ""; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
    private static string[] SteamLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            using var steam = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steam?.GetValue("SteamPath") is string path && Directory.Exists(path)) roots.Add(path.Replace('/', '\\'));
        } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
        string defaultPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(defaultPath)) roots.Add(defaultPath);
        foreach (var root in roots.ToArray()) {
            string text = ReadSmall(System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf"));
            foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"")) {
                string path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (!path.StartsWith(@"\\") && Directory.Exists(path)) roots.Add(path);
            }
        }
        return roots.Take(32).ToArray();
    }
    private static string[] FindInstalls(Game game, SaveGameRules? rule, string[] roots)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots) {
            foreach (string id in (rule?.SteamIds ?? []).Concat(game.SteamId is null ? [] : [game.SteamId]).Distinct()) {
                if (!id.All(char.IsAsciiDigit)) continue;
                string manifest = ReadSmall(System.IO.Path.Combine(root, "steamapps", "appmanifest_" + id + ".acf"));
                string dir = VdfValue(manifest, "installdir");
                if (dir.Length > 0 && dir == System.IO.Path.GetFileName(dir)) Add(System.IO.Path.Combine(root, "steamapps", "common", dir));
            }
        }
        string epic = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        try {
            if (Directory.Exists(epic)) foreach (string file in Directory.EnumerateFiles(epic, "*.item").Take(500)) {
                try {
                    using var document = JsonDocument.Parse(ReadSmall(file));
                    if (document.RootElement.TryGetProperty("DisplayName", out var title) && SameName(title.GetString() ?? "") && document.RootElement.TryGetProperty("InstallLocation", out var location)) Add(location.GetString() ?? "");
                } catch (JsonException) { }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 }) {
            try {
                using var registry = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (string key in uninstall.GetSubKeyNames().Take(1500)) {
                    using var entry = uninstall.OpenSubKey(key);
                    if (entry?.GetValue("DisplayName") is string name && SameName(name) && entry.GetValue("InstallLocation") is string path) Add(path);
                }
            } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return result.ToArray();
        bool SameName(string name) => name.Equals(game.Name, StringComparison.OrdinalIgnoreCase) || name.Equals(rule?.Name, StringComparison.OrdinalIgnoreCase) || rule?.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase) == true;
        void Add(string path) { if (System.IO.Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\") && Directory.Exists(path)) result.Add(System.IO.Path.GetFullPath(path)); }
    }
    private static bool RegistryKeyExists(string path)
    {
        string normalized = path.Replace('/', '\\');
        string prefix = normalized.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase) ? "HKEY_CURRENT_USER\\" : normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase) ? "HKEY_LOCAL_MACHINE\\" : "";
        if (prefix.Length == 0 || normalized.Contains('*') || normalized.Contains('<')) return false;
        try { using var key = (prefix == "HKEY_CURRENT_USER\\" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine).OpenSubKey(normalized[prefix.Length..]); return key is not null; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }
}
