using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mirage;

// Validated application commands and data exposed to the React interface.
// React receives data, never access to arbitrary native objects or executables.
public sealed partial class MirageController
{
    private static readonly string AppVersion = typeof(MirageController).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    private readonly SaveFinder saveFinder = new();
    private SaveLibraryService? saveLibraryService;
    private SaveLibraryService SaveLibrary => saveLibraryService ??= new SaveLibraryService(saveFinder, cachePath: System.IO.Path.Combine(Paths.Data, "save-library.json"));
    private SaveRestoreService? saveRestoreService;
    private SaveRestoreService SaveRestore => saveRestoreService ??= new SaveRestoreService(SaveLibrary, games);
    private SaveRestoreResult? lastRestore;
    private readonly Dictionary<string, string> saveInstallFolders = [];
    public void SetSaveInstallFolder(string gameId, string path) { WebGame(gameId); saveInstallFolders[gameId] = SaveFinder.ValidateFolder(path); }
    public SaveLocation ResolveSaveLocation(string id) => saveFinder.Resolve(id);
    public string GetSavePathToCopy(string id) => saveFinder.PathToCopy(id);
    public Task<SaveLibraryResult> ScanSaveLibraryAsync()
    {
        if (!PreviewOnly) return SaveLibrary.ScanAsync(games);
        var sample = new[] { "Cyberpunk 2077", "Stardew Valley", "The Witcher 3: Wild Hunt", "Resident Evil 4", "Minecraft" }.Select(name => games.FirstOrDefault(game => game.Name == name)).OfType<Game>().Select((game, index) => new InstalledSaveGame(game.Id, game.Name, game.Initial, game.IconUrl, game.HeroUrl, index == 4 ? null : @"C:\Games\" + game.Name, index == 3 ? 0 : index + 1, index == 3 ? 0 : 12 + index * 19, index == 3 ? 0 : 1_400_000L + index * 8_200_000L, index == 3 ? null : DateTimeOffset.UtcNow.AddDays(-index).AddHours(-2), index != 4)).ToArray();
        var changes = new[] { new SaveChange("preview-modified", sample[0].GameId, sample[0].Name, "modified", DateTimeOffset.UtcNow.AddMinutes(-18)), new SaveChange("preview-moved", sample[1].GameId, sample[1].Name, "moved", DateTimeOffset.UtcNow.AddDays(-1), @"C:\Old Saves", @"C:\New Saves") };
        return Task.FromResult(new SaveLibraryResult(sample, sample.Count(game => game.Installed), sample.Count(game => game.LocationCount > 0), sample.Count(game => !game.Installed), sample.Sum(game => game.FileCount), sample.Sum(game => game.SizeBytes), DateTimeOffset.UtcNow, false, changes));
    }
    public Task<SaveLibraryResult?> LoadSaveLibraryAsync() => Task.FromResult(PreviewOnly ? null : SaveLibrary.LoadCached());
    public Task<SaveLibraryResult> ValidateSaveLibraryAsync() => PreviewOnly ? ScanSaveLibraryAsync() : SaveLibrary.ValidateCachedAsync(games);
    public Task<SaveExportResult> ExportSaveLibraryAsync(string destination, bool zip) => SaveLibrary.ExportAsync(games, destination, zip);
    public string ResolveLastSaveExport() => SaveLibrary.ResolveLastExport();
    public Task<SaveRestoreAnalysis> AnalyzeSaveRestoreAsync(string path) => SaveRestore.AnalyzeAsync(path);
    public Task<SaveRestoreAnalysis?> AnalyzePendingSaveRestoreAsync() => PreviewOnly ? Task.FromResult<SaveRestoreAnalysis?>(null) : SaveRestore.AnalyzeLatestPendingAsync();
    public async Task<SaveRestoreResult> RestoreSavesAsync(string token, string mode) => lastRestore = await SaveRestore.RestoreAsync(token, mode);
    public string ResolveLastRestorePath(string kind)
    {
        string? path = kind == "rollback" ? lastRestore?.RollbackPath : kind == "pending" ? lastRestore?.PendingPath : null;
        return path is not null && (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)) ? path : throw new InvalidOperationException("Esse local de restauração não está mais disponível.");
    }
    private PlaylistWorkspace? playlistWorkspace;
    private PlaylistWorkspace Workspace => playlistWorkspace ??= new PlaylistWorkspace(prefs);
    public async Task UpdateLibraryInformationAsync()
    {
        if (PreviewOnly || quitting) return;
        try {
            string cache = System.IO.Path.Combine(Paths.Data, "catalog.json");
            if (!System.IO.File.Exists(cache) || DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(cache) > TimeSpan.FromHours(24)) {
                SetCatalog(await catalog.RefreshAsync(automaticRefresh.Token));
                if (selected is not null) selected = games.FirstOrDefault(g => g.Id == selected.Id) ?? selected;
                ShowSelection();
            }
        } catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or System.Threading.Tasks.TaskCanceledException or JsonException or UnauthorizedAccessException) { }
    }
    public bool HasWebSession => requested is not null;
    public bool HideWebOnMinimize => prefs.MinimizeToTray;
    private static string TextArg(JsonElement value, string name, string fallback = "") => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? fallback : fallback;
    private static int IntArg(JsonElement value, string name, int fallback = 0) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var p) && p.TryGetInt32(out int number) ? number : fallback;
    private static bool BoolArg(JsonElement value, string name, bool fallback = false) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;
    private static bool OptionalBool(JsonElement args, string name, bool previous) => !args.TryGetProperty(name, out var value) ? previous : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ArgumentException("Valor inválido para " + name + ".");
    private Game WebGame(string id) => gameById.GetValueOrDefault(id) ?? throw new ArgumentException("Este jogo não está no catálogo.");
    private int WebEntry(string id) { int i = prefs.ProcessPlaylist.FindIndex(p => p.EntryId == id); return i >= 0 ? i : throw new ArgumentException("Este item não está mais na sequência."); }
    private void WebEditable() { if (busy) throw new InvalidOperationException("Aguarde a operação atual."); if (playlistSession is not null) throw new InvalidOperationException("Encerre a sequência antes de editá-la."); }
    private static int WebMinutes(JsonElement args)
    {
        if (!args.TryGetProperty("minutes", out var value)) return 16;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) && n is >= 1 and <= 1440 ? n : throw new ArgumentException("Escolha um tempo inteiro de 1 a 1440 minutos.");
    }
    private object WebGameData(Game game) => new {
        game.Id, game.Name, game.Initial, game.IconUrl, game.HeroUrl, game.SupportsPlaylist,
        executables = ProcessSession.Options(game).Select(e => e.Name).ToArray()
    };
    public object SearchWeb(JsonElement args)
    {
        string query = TextArg(args, "query")[..Math.Min(256, TextArg(args, "query").Length)];
        string collection = TextArg(args, "collection", "explore");
        int offset = Math.Clamp(IntArg(args, "offset"), 0, games.Count);
        int limit = Math.Clamp(IntArg(args, "limit", 60), 1, 120);
        IEnumerable<Game> source = collection switch {
            "favorites" => games.Where(g => prefs.Favorites.Contains(g.Id)),
            "recent" => prefs.Recent.Select(id => gameById.GetValueOrDefault(id)).OfType<Game>(),
            "explore" => games,
            _ => throw new ArgumentException("Coleção inválida.")
        };
        if (!string.IsNullOrWhiteSpace(query)) source = GameSearch.Find(source, query);
        else if (collection == "explore") source = featured;
        if (BoolArg(args, "onlySequence")) source = source.Where(g => g.SupportsPlaylist);
        if (BoolArg(args, "hideQueued")) { var queued = prefs.ProcessPlaylist.Select(item => item.GameId).ToHashSet(); source = source.Where(game => !queued.Contains(game.Id)); }
        source = LibraryTools.Sort(source, TextArg(args, "sort", "default"));
        var matches = source.ToList();
        return new { items = matches.Skip(offset).Take(limit).Select(WebGameData).ToArray(), total = matches.Count, hasMore = offset + limit < matches.Count };
    }
    public object GetWebState()
    {
        var now = DateTimeOffset.UtcNow;
        var current = playlistSession?.Current;
        var next = current is not null ? prefs.ProcessPlaylist.ElementAtOrDefault(playlistSession!.Index + 1) : null;
        double elapsed = requested is not null && started != default ? Math.Max(0, (now - started).TotalSeconds) : 0;
        return new {
            version = AppVersion, preview = PreviewOnly, catalogCount, busy,
            storage = new { warning = persistenceWarning, replacementRequired = prefs.RequiresStorageReplacement },
            canUndoPlaylist = Workspace.CanUndo && playlistSession is null, undoLabel = Workspace.UndoLabel,
            savedPlaylists = prefs.SavedPlaylists.Select(saved => new { saved.Id, saved.Name, count = saved.Items.Count, totalMinutes = saved.Items.Sum(item => item.DurationMinutes), games = saved.Items.Take(3).Select(item => new { name = item.GameName, iconUrl = gameById.GetValueOrDefault(item.GameId)?.IconUrl }).ToArray() }).ToArray(),
            playlistTiming = new { totalMinutes = prefs.ProcessPlaylist.Sum(item => item.DurationMinutes), remainingSeconds = current is null ? prefs.ProcessPlaylist.Sum(item => item.DurationMinutes) * 60d : Math.Max(0, playlistSession!.Remaining(now).TotalSeconds) + prefs.ProcessPlaylist.Skip(playlistSession!.Index + 1).Sum(item => item.DurationMinutes) * 60d },
            selected = selected is not null ? WebGameData(selected) : null,
            variantIndex = executableIndex,
            preferences = new { minutes = prefs.PlaylistDefaultMinutes, showWindow = prefs.ProcessWindow, minimizeToTray = prefs.MinimizeToTray, notifications = prefs.Notifications },
            favorites = prefs.Favorites.ToArray(), recentCount = prefs.Recent.Count,
            playlist = prefs.ProcessPlaylist.Select((p, i) => new {
                p.EntryId, p.GameId, p.GameName, p.Executable, p.DurationMinutes, p.ShowWindow,
                iconUrl = gameById.GetValueOrDefault(p.GameId)?.IconUrl,
                position = i + 1,
                status = current is null ? "queued" : i < playlistSession!.Index ? "done" : i == playlistSession.Index ? "playing" : "next"
            }).ToArray(),
            session = requested is null ? null : new {
                game = WebGameData(requested), confirmed = active is not null,
                elapsedSeconds = elapsed, isPlaylist = current is not null,
                index = playlistSession?.Index ?? 0, count = playlistSession?.Count ?? 1,
                remainingSeconds = current is null ? 0 : playlistSession!.Remaining(now).TotalSeconds,
                durationMinutes = current?.DurationMinutes ?? 0,
                next = next is null ? null : new { name = next.GameName, minutes = next.DurationMinutes },
                itemProgress = current is null ? 0 : Math.Clamp(1 - playlistSession!.Remaining(now).TotalMinutes / current.DurationMinutes, 0, 1)
            },
            status = status is { } message ? new { title = message.Title, description = message.Description } : null
        };
    }
    public async Task<object> DispatchWebAsync(string command, JsonElement args)
    {
        ObjectDisposedException.ThrowIf(quitting, this);
        switch (command) {
            case "saves.load": { var cached = await LoadSaveLibraryAsync(); if (cached is null) return new { empty = true }; return cached; }
            case "saves.validate": return await ValidateSaveLibraryAsync();
            case "saves.scanInstalled": return await ScanSaveLibraryAsync();
            case "saves.restore.pending": { var pendingRestore = await AnalyzePendingSaveRestoreAsync(); return pendingRestore is null ? new { empty = true } : pendingRestore; }
            case "saves.restore.apply": return await RestoreSavesAsync(TextArg(args, "token"), TextArg(args, "mode", "merge"));
            case "saves.find": {
                var game = WebGame(TextArg(args, "gameId"));
                string? folder = null;
                if (BoolArg(args, "manual") && !saveInstallFolders.TryGetValue(game.Id, out folder)) throw new ArgumentException("Escolha a pasta do jogo primeiro.");
                return await saveFinder.FindAsync(game, folder);
            }
            case "boot": return new { state = GetWebState(), results = SearchWeb(args) };
            case "retrySave": SavePreferences(); break;
            case "replaceUnreadablePreferences":
                if (!prefs.RequiresStorageReplacement) throw new InvalidOperationException("O arquivo de preferências não precisa ser substituído.");
                SavePreferences(replaceUnreadable: true); break;
            case "search": return SearchWeb(args);
            case "select": selected = WebGame(TextArg(args, "gameId")); ShowSelection(); break;
            case "favorite": {
                var game = WebGame(TextArg(args, "gameId"));
                if (!prefs.Favorites.Add(game.Id)) prefs.Favorites.Remove(game.Id);
                SavePreferences(); ShowSelection(); break;
            }
            case "configure": {
                int duration = args.TryGetProperty("minutes", out _) ? WebMinutes(args) : prefs.PlaylistDefaultMinutes;
                bool showWindow = OptionalBool(args, "showWindow", prefs.ProcessWindow);
                bool minimizeToTray = OptionalBool(args, "minimizeToTray", prefs.MinimizeToTray);
                bool notifications = OptionalBool(args, "notifications", prefs.Notifications);
                prefs.PlaylistDefaultMinutes = duration; prefs.PlaylistDefaultsVersion = 1;
                prefs.ProcessWindow = showWindow;
                prefs.MinimizeToTray = minimizeToTray; prefs.Notifications = notifications;
                SavePreferences(); break;
            }
            case "variant": WebEditable(); executableIndex = Math.Clamp(IntArg(args, "index"), 0, Math.Max(0, processOptions.Count - 1)); break;
            case "add": {
                WebEditable(); var game = WebGame(TextArg(args, "gameId"));
                if (!game.SupportsPlaylist) throw new ArgumentException("Este jogo não possui um processo compatível.");
                if (prefs.ProcessPlaylist.Count >= PlaylistWorkspace.MaxEntries) throw new InvalidOperationException("Uma sequência pode conter até 500 jogos.");
                AddGameToPlaylist(game, WebMinutes(args)); break;
            }
            case "addMany": {
                WebEditable();
                if (!args.TryGetProperty("gameIds", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > LibraryTools.MaxSelection) throw new ArgumentException("Selecione de 1 a 100 jogos por vez.");
                var ids = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException("Seleção inválida.")).ToArray();
                int minutes = WebMinutes(args);
                var additions = LibraryTools.PrepareAddition(games, ids, prefs.ProcessPlaylist.Count, minutes, prefs.ProcessWindow, selected?.Id, executableIndex);
                Workspace.Remember("Adicionar seleção de jogos"); prefs.ProcessPlaylist.AddRange(additions);
                prefs.PlaylistDefaultMinutes = minutes; prefs.PlaylistDefaultsVersion = 1;
                SavePreferences(); break;
            }
            case "remove": { WebEditable(); int index = WebEntry(TextArg(args, "entryId")); Workspace.Remember("Remover jogo"); prefs.ProcessPlaylist.RemoveAt(index); SavePreferences(); break; }
            case "duration": { WebEditable(); var item = prefs.ProcessPlaylist[WebEntry(TextArg(args, "entryId"))]; int minutes = WebMinutes(args); if (item.DurationMinutes != minutes) { Workspace.Remember("Alterar duração"); item.DurationMinutes = minutes; SavePreferences(); } break; }
            case "move": {
                WebEditable(); int i = WebEntry(TextArg(args, "entryId")); int delta = IntArg(args, "delta");
                if (delta is not (-1 or 1)) throw new ArgumentException("Ordem inválida.");
                int to = Math.Clamp(i + delta, 0, prefs.ProcessPlaylist.Count - 1);
                if (to == i) break;
                Workspace.Remember("Reordenar jogos");
                var item = prefs.ProcessPlaylist[i]; prefs.ProcessPlaylist.RemoveAt(i); prefs.ProcessPlaylist.Insert(to, item);
                SavePreferences(); break;
            }
            case "clear": WebEditable(); if (prefs.ProcessPlaylist.Count > 0) { Workspace.Remember("Limpar sequência"); prefs.ProcessPlaylist.Clear(); SavePreferences(); } break;
            case "undoPlaylist": WebEditable(); Workspace.Undo(); SavePreferences(); break;
            case "durationAll": WebEditable(); Workspace.SetAllDurations(WebMinutes(args)); SavePreferences(); break;
            case "savePlaylist": WebEditable(); Workspace.Save(TextArg(args, "name")); SavePreferences(); break;
            case "renamePlaylist": WebEditable(); Workspace.Rename(TextArg(args, "savedId"), TextArg(args, "name")); SavePreferences(); break;
            case "deletePlaylist": WebEditable(); Workspace.Delete(TextArg(args, "savedId")); SavePreferences(); break;
            case "usePlaylist": {
                WebEditable(); string merge = TextArg(args, "merge");
                if (merge is not ("replace" or "append")) throw new ArgumentException("Escolha como carregar a sequência.");
                var saved = Workspace.Find(TextArg(args, "savedId"));
                foreach (var entry in saved.Items.Concat(merge == "append" ? prefs.ProcessPlaylist : [])) {
                    var game = WebGame(entry.GameId);
                    if (!ProcessSession.Options(game).Any(option => option.Name.Equals(entry.Executable, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"{entry.GameName} mudou no catálogo. Monte uma nova sequência com esse jogo.");
                }
                Workspace.Use(saved.Id, merge == "append");
                foreach (var entry in prefs.ProcessPlaylist) entry.GameName = WebGame(entry.GameId).Name;
                SavePreferences(); break;
            }
            case "startPlaylist":
                WebEditable(); if (PreviewOnly) throw new InvalidOperationException("A prévia não inicia processos reais.");
                if (prefs.ProcessPlaylist.Count == 0) throw new InvalidOperationException("Adicione jogos antes de iniciar.");
                await StartPlaylistAsync(); if (playlistSession is not null) Workspace.ClearHistory(); break;
            case "activate":
                WebEditable(); if (PreviewOnly) throw new InvalidOperationException("A prévia não inicia processos reais.");
                if (selected is null) throw new InvalidOperationException("Selecione um jogo.");
                await StartProcessAsync();
                break;
            case "stop": if (busy) throw new InvalidOperationException("Aguarde a operação atual."); await StopAsync(); break;
            case "refresh":
                if (busy) throw new InvalidOperationException("Aguarde a operação atual.");
                busy = true; try { SetCatalog(await catalog.RefreshAsync()); if (selected is not null) selected = games.FirstOrDefault(g => g.Id == selected.Id) ?? selected; ShowSelection(); Status("Catálogo atualizado", $"{games.Count:N0} jogos disponíveis.", true); } finally { busy = false; } break;
            case "previewSession" when PreviewOnly: PreviewSessionLayout(); Workspace.ClearHistory(); break;
            default: throw new ArgumentException("Ação não reconhecida.");
        }
        return GetWebState();
    }
}
