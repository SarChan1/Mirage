using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mirage;

public sealed record SaveRestoreGame(string GameId, string Name, string Status, string? Target, bool Installed, int Files, long SizeBytes, int Conflicts, string Note, bool ProfileMigration = false, string? PreviousPath = null, int IdenticalFiles = 0);
public sealed record SaveRestoreAnalysis(string Token, string SourceName, DateTimeOffset? CreatedAt, SaveRestoreGame[] Games, int Files, long SizeBytes, int ReadyGames, int PendingGames, int Conflicts, bool Legacy, int ProfileMigrations = 0, bool PendingPackage = false, int CurrentGames = 0);
public sealed record SaveRestoreResult(int RestoredFiles, int PreservedConflicts, int PendingFiles, string? RollbackPath, string? PendingPath, SaveRestoreAnalysis Analysis);

public sealed class SaveRestoreService
{
    private sealed record RestoreFile(string Source, string Relative, long Size);
    private sealed record RestoreLocation(string BackupPath, string? OriginalPath, string Kind, string? Target, RestoreFile[] Files, bool ProfileMigration = false);
    private sealed record RestoreGameState(string GameId, string Name, bool Installed, RestoreLocation[] Locations);
    private sealed record RestoreState(string Token, string Source, bool Zip, DateTimeOffset? CreatedAt, bool Legacy, RestoreGameState[] Games, SaveRestoreAnalysis Analysis);
    private readonly SaveLibraryService library;
    private readonly IReadOnlyList<Game> catalog;
    private readonly string dataRoot;
    private readonly Dictionary<string, RestoreState> states = [];

    public SaveRestoreService(SaveLibraryService library, IEnumerable<Game> catalog, string? dataRoot = null) { this.library = library; this.catalog = catalog.ToArray(); this.dataRoot = dataRoot ?? Paths.Data; }

    public async Task<SaveRestoreAnalysis> AnalyzeAsync(string source)
    {
        source = Path.GetFullPath(source);
        bool zip = File.Exists(source) && Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        if (!zip && !Directory.Exists(source)) throw new ArgumentException("Escolha um ZIP ou uma pasta de backup existente.");
        if (zip && new FileInfo(source).Length > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("O arquivo de backup é maior que o limite de 8 GB.");
        var parsed = await Task.Run(() => ReadManifest(source, zip));
        var current = library.Current;
        var currentById = current?.Games.ToDictionary(game => game.GameId) ?? [];
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var statesForGames = new List<RestoreGameState>(); var summaries = new List<SaveRestoreGame>();
        int totalFiles = 0, totalConflicts = 0; long totalBytes = 0;
        long comparisonBudget = 512L * 1024 * 1024;
        using var analysisArchive = zip ? ZipFile.OpenRead(source) : null;
        foreach (var item in parsed.Games.Take(500)) {
            var game = FindGame(item.Id, item.Name);
            string id = game?.Id ?? item.Id ?? "legacy-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.Name)))[..16].ToLowerInvariant();
            var known = game is null ? [] : library.KnownLocations(game.Id).Where(location => location.Kind != "registry").ToArray();
            bool installed = currentById.GetValueOrDefault(id)?.Installed == true;
            var locations = new List<RestoreLocation>();
            foreach (var sourceLocation in item.Locations.Take(160)) {
                var files = EnumerateFiles(source, zip, sourceLocation.BackupPath).Take(100_001).ToArray();
                if (files.Length > 100_000) throw new InvalidDataException("O backup contém arquivos demais para uma restauração segura.");
                var match = MatchTarget(sourceLocation, known, usedTargets, parsed.CreatedAt);
                string? target = !installed && known.Length == 0 ? null : match.Path;
                if (target is not null) usedTargets.Add(target);
                locations.Add(new(sourceLocation.BackupPath, sourceLocation.OriginalPath, sourceLocation.Kind, target, files, target is not null && match.ProfileMigration));
            }
            int filesCount = locations.Sum(location => location.Files.Length); long bytes = locations.Sum(location => location.Files.Sum(file => file.Size));
            int existing = locations.Sum(CountConflicts), identical = 0;
            foreach (var location in locations.Where(location => location.Target is not null)) foreach (var file in location.Files) {
                string targetFile = TargetFile(location, file);
                if (!File.Exists(targetFile) || !SameLength(targetFile, file.Size) || file.Size > comparisonBudget) continue;
                comparisonBudget -= file.Size;
                if (await SameContentAsync(analysisArchive, zip, file, targetFile)) identical++;
            }
            int conflicts = Math.Max(0, existing - identical);
            bool migration = locations.Any(location => location.ProfileMigration);
            bool alreadyCurrent = filesCount > 0 && identical == filesCount;
            string status = locations.Count == 0 ? "invalid" : alreadyCurrent ? "current" : locations.All(location => location.Target is not null) ? (conflicts > 0 ? "conflict" : migration ? "migration" : "ready") : "pending";
            string note = status switch {
                "pending" when parsed.PendingPackage && installed => "O backup está protegido. Abra o jogo e crie um save para ele gerar a pasta atual; depois verifique novamente.",
                "pending" when parsed.PendingPackage => "O backup está protegido. Instale o jogo quando quiser; nenhum arquivo será devolvido ao caminho antigo.",
                "pending" when installed => "O jogo está instalado, mas ainda não criou uma pasta atual. Ao continuar, o Mirage guardará este save para depois.",
                "pending" => "O jogo não está instalado. Ao continuar, o Mirage guardará este save até existir um destino atual.",
                "conflict" when migration => "Uma pasta de perfil nova foi detectada e já possui saves. A correção pode mesclar ou preservar os conflitos.",
                "conflict" => "Já existem saves nesse destino. Você escolherá como tratar os conflitos.",
                "migration" => "A pasta antiga mudou. Uma correção está disponível para inserir os saves no perfil atual.",
                "current" => "Os saves já estão presentes e iguais na pasta atual. Nenhuma correção é necessária.",
                "invalid" => "Nenhum arquivo restaurável foi encontrado para este jogo.",
                _ => parsed.PendingPackage ? "A pasta atual foi encontrada. O save guardado está pronto para ser inserido." : "Destino atual reconhecido e pronto para restauração."
            };
            statesForGames.Add(new(id, item.Name, installed, locations.ToArray()));
            bool needsMigration = migration && !alreadyCurrent;
            string? primaryTarget = status == "pending" ? null : locations.FirstOrDefault(location => location.ProfileMigration)?.Target ?? locations.Select(location => location.Target).FirstOrDefault(target => target is not null);
            summaries.Add(new(id, item.Name, status, primaryTarget, installed, filesCount, bytes, conflicts, note, needsMigration, needsMigration ? locations.FirstOrDefault(location => location.ProfileMigration)?.OriginalPath : null, identical));
            totalFiles += filesCount; totalBytes += bytes; totalConflicts += conflicts;
        }
        if (totalFiles == 0) throw new InvalidDataException("O backup não contém arquivos de save restauráveis.");
        if (totalFiles > 100_000 || totalBytes > 20L * 1024 * 1024 * 1024) throw new InvalidDataException("O conteúdo do backup ultrapassa o limite seguro de restauração.");
        string token = Guid.NewGuid().ToString("N");
        var analysis = new SaveRestoreAnalysis(token, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)), parsed.CreatedAt, summaries.ToArray(), totalFiles, totalBytes, summaries.Count(game => game.Status is "ready" or "migration" or "conflict"), summaries.Count(game => game.Status == "pending"), totalConflicts, parsed.Legacy, summaries.Count(game => game.ProfileMigration), parsed.PendingPackage, summaries.Count(game => game.Status == "current"));
        states.Clear(); states[token] = new(token, source, zip, parsed.CreatedAt, parsed.Legacy, statesForGames.ToArray(), analysis);
        return analysis;
    }

    public async Task<SaveRestoreResult> RestoreAsync(string token, string mode)
    {
        if (!states.TryGetValue(token, out var state)) throw new ArgumentException("Analise o backup novamente antes de restaurar.");
        if (mode is not ("merge" or "replace")) throw new ArgumentException("Escolha uma forma válida de restauração.");
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss_fff");
        string pendingRoot = UniqueFolder(Path.Combine(dataRoot, "Imported Saves", stamp));
        string rollbackPath = Path.Combine(dataRoot, "Restore Points", "Mirage-Restore-" + stamp + ".zip");
        int restored = 0, preserved = 0, pending = 0; ZipArchive? rollback = null;
        var rollbackEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingLocations = new List<(RestoreGameState Game, RestoreLocation Location, string Category)>();
        try {
            using var sourceArchive = state.Zip ? ZipFile.OpenRead(state.Source) : null;
            foreach (var game in state.Games) foreach (var location in game.Locations) {
                string? pendingCategory = null;
                foreach (var file in location.Files) {
                    if (location.Target is null) { pendingCategory = "Pending"; await CopyToVault(sourceArchive, state, game, location, file, pendingRoot, pendingCategory); pending++; continue; }
                    string target = TargetFile(location, file);
                    if (!SafeRestoreTarget(target)) { pendingCategory = "Review"; await CopyToVault(sourceArchive, state, game, location, file, pendingRoot, pendingCategory); pending++; continue; }
                    if (File.Exists(target)) {
                        if (SameLength(target, file.Size) && await SameContentAsync(sourceArchive, state, file, target)) continue;
                        if (mode == "merge") { await CopyToVault(sourceArchive, state, game, location, file, pendingRoot, "Conflicts"); preserved++; continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(rollbackPath)!);
                        rollback ??= ZipFile.Open(rollbackPath, ZipArchiveMode.Create);
                        string rollbackEntry = SafePart(game.Name) + "/" + SafePart(Path.GetFileName(location.Target)) + "/" + file.Relative.Replace('\\', '/');
                        rollback.CreateEntryFromFile(target, UniqueEntry(rollbackEntries, rollbackEntry), CompressionLevel.Optimal);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await CopySourceAsync(sourceArchive, state, file, target, overwrite: true);
                    restored++;
                }
                if (pendingCategory is not null) pendingLocations.Add((game, location, pendingCategory));
            }
        } finally { rollback?.Dispose(); }
        if (pendingLocations.Count > 0) await WritePendingManifestAsync(pendingRoot, pendingLocations);
        if (IsPendingSource(state.Source)) await File.WriteAllTextAsync(Path.Combine(state.Source, ".completed"), DateTimeOffset.UtcNow.ToString("O"));
        if (!Directory.Exists(pendingRoot)) pendingRoot = "";
        if (!File.Exists(rollbackPath)) rollbackPath = "";
        return new(restored, preserved, pending, rollbackPath.Length > 0 ? rollbackPath : null, pendingRoot.Length > 0 ? pendingRoot : null, state.Analysis);
    }

    public async Task<SaveRestoreAnalysis?> AnalyzeLatestPendingAsync()
    {
        string root = Path.Combine(dataRoot, "Imported Saves");
        if (!Directory.Exists(root)) return null;
        foreach (string folder in Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(100)) {
            if (!File.Exists(Path.Combine(folder, "manifest.json")) || File.Exists(Path.Combine(folder, ".completed"))) continue;
            try { return await AnalyzeAsync(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException) { }
        }
        return null;
    }

    private async Task WritePendingManifestAsync(string root, IEnumerable<(RestoreGameState Game, RestoreLocation Location, string Category)> entries)
    {
        var games = entries.GroupBy(entry => (entry.Game.GameId, entry.Game.Name)).Select(group => new {
            gameId = group.Key.GameId,
            name = group.Key.Name,
            installed = group.First().Game.Installed,
            locations = group.Select(entry => {
                string stored = Path.Combine(entry.Category, SafePart(entry.Game.Name), SafePart(Path.GetFileName(entry.Location.BackupPath)));
                if (entry.Location.Kind == "file" && entry.Location.Files.Length > 0) stored = Path.Combine(stored, entry.Location.Files[0].Relative);
                return new { backupPath = stored.Replace('\\', '/'), originalPath = entry.Location.OriginalPath, kind = entry.Location.Kind };
            }).ToArray()
        }).ToArray();
        int files = entries.Sum(entry => entry.Location.Files.Length);
        long size = entries.Sum(entry => entry.Location.Files.Sum(file => file.Size));
        var manifest = new { schemaVersion = 2, createdAt = DateTimeOffset.UtcNow, files, sizeBytes = size, pendingRestore = true, games };
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool IsPendingSource(string source)
    {
        if (!Directory.Exists(source) || !File.Exists(Path.Combine(source, "manifest.json"))) return false;
        string root = Path.GetFullPath(Path.Combine(dataRoot, "Imported Saves")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(source).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ManifestLocation(string BackupPath, string? OriginalPath, string Kind);
    private sealed record ManifestGame(string? Id, string Name, ManifestLocation[] Locations);
    private sealed record ParsedManifest(DateTimeOffset? CreatedAt, bool Legacy, bool PendingPackage, ManifestGame[] Games);

    private static ParsedManifest ReadManifest(string source, bool zip)
    {
        string json;
        if (zip) {
            using var archive = ZipFile.OpenRead(source);
            var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Este ZIP não possui o manifesto de um backup do Mirage.");
            if (entry.Length > 2_000_000) throw new InvalidDataException("O manifesto do backup é maior que o permitido.");
            using var reader = new StreamReader(entry.Open()); json = reader.ReadToEnd();
        } else {
            string manifest = Path.Combine(source, "manifest.json");
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > 2_000_000) throw new InvalidDataException("Esta pasta não possui um manifesto válido do Mirage.");
            json = File.ReadAllText(manifest);
        }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        int schema = root.TryGetProperty("schemaVersion", out var schemaValue) && schemaValue.TryGetInt32(out int version) ? version : 1;
        DateTimeOffset? created = root.TryGetProperty("createdAt", out var createdValue) && createdValue.TryGetDateTimeOffset(out var date) ? date : null;
        bool pendingPackage = root.TryGetProperty("pendingRestore", out var pendingValue) && pendingValue.ValueKind == JsonValueKind.True;
        if (!root.TryGetProperty("games", out var gamesValue) || gamesValue.ValueKind != JsonValueKind.Array) throw new InvalidDataException("O manifesto não possui uma lista de jogos.");
        var games = new List<ManifestGame>();
        foreach (var game in gamesValue.EnumerateArray()) {
            string name = game.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "Jogo" : game.TryGetProperty("game", out var legacyName) ? legacyName.GetString() ?? "Jogo" : "Jogo";
            string? id = game.TryGetProperty("gameId", out var idValue) ? idValue.GetString() : null;
            var locations = new List<ManifestLocation>();
            if (schema >= 2 && game.TryGetProperty("locations", out var locationValues) && locationValues.ValueKind == JsonValueKind.Array) {
                foreach (var location in locationValues.EnumerateArray()) {
                    string backup = location.TryGetProperty("backupPath", out var backupValue) ? NormalizeEntry(backupValue.GetString() ?? "") : "";
                    string? original = location.TryGetProperty("originalPath", out var originalValue) ? originalValue.GetString() : null;
                    string kind = location.TryGetProperty("kind", out var kindValue) && kindValue.GetString() == "file" ? "file" : "folder";
                    if (backup.Length > 0) locations.Add(new(backup, original, kind));
                }
            } else {
                foreach (string child in TopLevelLocations(source, zip, SafePart(name))) locations.Add(new(child, null, "folder"));
            }
            games.Add(new(id, name, locations.ToArray()));
        }
        return new(created, schema < 2, pendingPackage, games.ToArray());
    }

    private Game? FindGame(string? id, string name) => (!string.IsNullOrEmpty(id) ? catalog.FirstOrDefault(game => game.Id == id) : null) ?? catalog.FirstOrDefault(game => game.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private sealed record TargetMatch(string? Path, bool ProfileMigration, int Score);
    private static TargetMatch MatchTarget(ManifestLocation source, SaveLocation[] known, HashSet<string> used, DateTimeOffset? backupCreated)
    {
        string? original = source.OriginalPath is not null && SafeRestoreTarget(source.OriginalPath) ? Path.GetFullPath(source.OriginalPath) : null;
        var candidates = known.Where(location => location.Kind == source.Kind && !used.Contains(location.Path)).ToArray();
        var exact = original is null ? null : candidates.FirstOrDefault(location => location.Path.Equals(original, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) {
            var newerProfile = candidates.Where(location => !location.Path.Equals(exact.Path, StringComparison.OrdinalIgnoreCase) && ProfileShape(location.Path).Equals(ProfileShape(exact.Path), StringComparison.OrdinalIgnoreCase) && location.LastModified is not null)
                .OrderByDescending(location => location.LastModified).FirstOrDefault();
            if (newerProfile?.LastModified is { } newer && exact.LastModified is { } older && newer > older.AddMinutes(2) && (backupCreated is null || newer > backupCreated.Value.AddMinutes(-2))) return new(newerProfile.Path, true, 140);
            return new(exact.Path, false, 160);
        }
        var ranked = candidates.Select(location => (Location: location, Score: Similarity(original, location.Path), Modified: location.LastModified ?? DateTimeOffset.MinValue)).OrderByDescending(item => item.Score).ThenByDescending(item => item.Modified).ToArray();
        if (ranked.Length > 0) {
            var best = ranked[0];
            bool clear = ranked.Length == 1 || best.Score >= ranked[1].Score + 8 || best.Modified > ranked[1].Modified.AddMinutes(2);
            if ((original is null && clear) || best.Score >= 45) return new(best.Location.Path, original is not null && !best.Location.Path.Equals(original, StringComparison.OrdinalIgnoreCase), best.Score);
        }
        if (original is not null && (File.Exists(original) || Directory.Exists(original))) return new(original, false, 20);
        return new(null, false, 0);
    }
    private static int Similarity(string? original, string candidate)
    {
        if (original is null) return 30;
        if (ProfileShape(original).Equals(ProfileShape(candidate), StringComparison.OrdinalIgnoreCase)) return 120;
        string? originalParent = Path.GetDirectoryName(original), candidateParent = Path.GetDirectoryName(candidate);
        int score = originalParent?.Equals(candidateParent, StringComparison.OrdinalIgnoreCase) == true ? 85 : 0;
        var left = original.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var right = candidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 1; i <= Math.Min(left.Length, right.Length); i++) { if (!left[^i].Equals(right[^i], StringComparison.OrdinalIgnoreCase) && !(Volatile(left[^i]) && Volatile(right[^i]))) break; score += 8; }
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++) { if (!left[i].Equals(right[i], StringComparison.OrdinalIgnoreCase) && !(Volatile(left[i]) && Volatile(right[i]))) break; score += 4; }
        return score;
    }
    private static string ProfileShape(string path) => string.Join(Path.DirectorySeparatorChar, path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(part => Volatile(part) ? "{profile}" : part));
    private static bool Volatile(string part) => part.Length >= 4 && (part.All(char.IsAsciiDigit) || Guid.TryParse(part, out _) || (part.Length >= 8 && part.All(char.IsAsciiHexDigit)) || Regex.IsMatch(part, "^(?:profile|user|account|slot)[-_]?[a-z0-9]{4,}$", RegexOptions.IgnoreCase));
    private static int CountConflicts(RestoreLocation location) => location.Target is null ? 0 : location.Files.Count(file => File.Exists(TargetFile(location, file)));
    private static string TargetFile(RestoreLocation location, RestoreFile file) => location.Kind == "file" ? location.Target! : Path.Combine(location.Target!, file.Relative);
    private static bool SameLength(string path, long length) { try { return new FileInfo(path).Length == length; } catch { return false; } }

    private static IEnumerable<RestoreFile> EnumerateFiles(string source, bool zip, string backupPath)
    {
        backupPath = NormalizeEntry(backupPath);
        if (zip) {
            using var archive = ZipFile.OpenRead(source);
            foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0)) {
                string name = NormalizeEntry(entry.FullName);
                if (name.Equals(backupPath, StringComparison.OrdinalIgnoreCase)) yield return new(name, Path.GetFileName(name), entry.Length);
                else if (name.StartsWith(backupPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) yield return new(name, name[(backupPath.TrimEnd('/').Length + 1)..].Replace('/', Path.DirectorySeparatorChar), entry.Length);
            }
        } else {
            string root = Path.GetFullPath(Path.Combine(source, backupPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!root.StartsWith(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) yield break;
            if (File.Exists(root)) { yield return new(root, Path.GetFileName(root), new FileInfo(root).Length); yield break; }
            if (!Directory.Exists(root)) yield break;
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true, MaxRecursionDepth = 32 };
            foreach (string file in Directory.EnumerateFiles(root, "*", options)) yield return new(file, Path.GetRelativePath(root, file), new FileInfo(file).Length);
        }
    }

    private static IEnumerable<string> TopLevelLocations(string source, bool zip, string game)
    {
        string prefix = NormalizeEntry(game).TrimEnd('/') + "/";
        if (zip) { using var archive = ZipFile.OpenRead(source); return archive.Entries.Select(entry => NormalizeEntry(entry.FullName)).Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(path => path.Split('/').Take(2)).Where(parts => parts.Count() == 2).Select(parts => string.Join('/', parts)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        string folder = Path.Combine(source, game); return Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0).Select(path => game + "/" + Path.GetFileName(path)).ToArray() : [];
    }

    private static string NormalizeEntry(string value)
    {
        string path = value.Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || path.Length > 600 || path.Split('/').Any(part => part is "" or "." or "..")) return "";
        return path;
    }
    private static string SafePart(string value) { string part = string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.'); return string.IsNullOrWhiteSpace(part) ? "Game" : part[..Math.Min(part.Length, 100)]; }
    private static bool SafeRestoreTarget(string path)
    {
        try {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            string full = Path.GetFullPath(path);
            string home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string shared = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(home, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(shared, StringComparison.OrdinalIgnoreCase)) return false;
            string? current = File.Exists(full) ? Path.GetDirectoryName(full) : full;
            while (!string.IsNullOrEmpty(current) && (current.StartsWith(home, StringComparison.OrdinalIgnoreCase) || current.StartsWith(shared, StringComparison.OrdinalIgnoreCase))) { if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false; current = Path.GetDirectoryName(current); }
            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    private static string UniqueEntry(HashSet<string> entries, string name) { string value = name.Replace('\\', '/'); if (entries.Add(value)) return value; string stem = Path.GetFileNameWithoutExtension(value), extension = Path.GetExtension(value); string? folder = Path.GetDirectoryName(value)?.Replace('\\', '/'); for (int i = 2; i < 10000; i++) { string candidate = (string.IsNullOrEmpty(folder) ? "" : folder + "/") + stem + "-" + i + extension; if (entries.Add(candidate)) return candidate; } throw new IOException("Não foi possível criar a cópia de segurança dos arquivos atuais."); }

    private static async Task<bool> SameContentAsync(ZipArchive? archive, RestoreState state, RestoreFile source, string target)
        => await SameContentAsync(archive, state.Zip, source, target);
    private static async Task<bool> SameContentAsync(ZipArchive? archive, bool zip, RestoreFile source, string target)
    {
        await using var left = OpenSource(archive, zip, source); await using var right = File.OpenRead(target);
        byte[] a = new byte[64 * 1024], b = new byte[64 * 1024];
        while (true) { int an = await left.ReadAsync(a), bn = await right.ReadAsync(b); if (an != bn) return false; if (an == 0) return true; if (!a.AsSpan(0, an).SequenceEqual(b.AsSpan(0, bn))) return false; }
    }
    private static Stream OpenSource(ZipArchive? archive, RestoreState state, RestoreFile source) => OpenSource(archive, state.Zip, source);
    private static Stream OpenSource(ZipArchive? archive, bool zip, RestoreFile source) => zip ? (archive?.GetEntry(source.Source) ?? throw new InvalidDataException("Um arquivo esperado não existe mais no ZIP.")).Open() : File.OpenRead(source.Source);
    private static async Task CopySourceAsync(ZipArchive? archive, RestoreState state, RestoreFile source, string target, bool overwrite)
    {
        if (!overwrite && File.Exists(target)) return;
        await using var input = OpenSource(archive, state, source); await using var output = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true); await input.CopyToAsync(output);
    }
    private static async Task CopyToVault(ZipArchive? archive, RestoreState state, RestoreGameState game, RestoreLocation location, RestoreFile file, string root, string category)
    {
        string target = Path.Combine(root, category, SafePart(game.Name), SafePart(Path.GetFileName(location.BackupPath)), file.Relative);
        target = UniqueFile(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await CopySourceAsync(archive, state, file, target, false);
    }
    private static string UniqueFile(string path) { if (!File.Exists(path)) return path; string folder = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path); for (int i = 2; i < 10000; i++) { string candidate = Path.Combine(folder, stem + "-" + i + extension); if (!File.Exists(candidate)) return candidate; } throw new IOException("Não foi possível guardar um arquivo importado."); }
    private static string UniqueFolder(string path) { if (!Directory.Exists(path) && !File.Exists(path)) return path; for (int i = 2; i < 10000; i++) { string candidate = path + "-" + i; if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate; } throw new IOException("Não foi possível criar a pasta para os saves pendentes."); }
}
