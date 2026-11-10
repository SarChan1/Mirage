using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mirage;

// Application state and session lifecycle. No window, controls or UI framework are created here.
public sealed partial class MirageController : IDisposable
{
    private readonly Action<Action> post;
    private readonly CatalogService catalog = new();
    private readonly Preferences prefs;
    private readonly ProcessSession processSession = new();
    private List<Game> games = [];
    private Dictionary<string, Game> gameById = [];
    private Game[] featured = [];
    private bool librarySuspended;
    private int catalogCount;
    private readonly System.Threading.CancellationTokenSource automaticRefresh = new();
    public bool IsLibrarySuspended => librarySuspended;
    public int LoadedGameCount => games.Count;
    private Game? selected, requested, active;
    private List<GameExecutable> processOptions = [];
    private string? executableGameId;
    private int executableIndex, generation;
    private bool busy, quitting, initialized;
    private ProcessPlaylist? playlistSession;
    private DateTimeOffset started;
    private (string Title, string Description)? status;
    private string? persistenceWarning;
    private string locale = "pt-BR";
    public bool PreviewOnly { get; }
    public event Action<string>? TrayTextChanged;
    public event Action<string, string>? NotificationRaised;
    private static readonly string[] Highlights = ["Cyberpunk 2077", "ELDEN RING", "Red Dead Redemption 2", "Baldur's Gate 3", "Grand Theft Auto V", "Hollow Knight: Silksong", "Minecraft", "VALORANT", "Counter-Strike 2", "Stardew Valley", "Clair Obscur: Expedition 33", "The Witcher 3: Wild Hunt", "Hades II", "Fortnite", "Terraria", "No Man's Sky", "Forza Horizon 5", "Resident Evil 4"];

    public MirageController(bool preview, Action<Action> dispatch, Preferences? preferences = null)
    {
        PreviewOnly = preview;
        post = dispatch;
        prefs = preferences ?? Preferences.Load();
        persistenceWarning = prefs.LoadWarning;
        processSession.Exited += () => post(() => {
            if (quitting || requested is null) return;
            bool wasPlaylist = playlistSession is not null;
            generation++; requested = null; active = null; playlistSession = null;
            Status(wasPlaylist ? "A lista foi interrompida" : "O processo foi encerrado", wasPlaylist ? "O processo atual fechou antes do tempo. Revise o item e inicie a lista novamente." : "Você pode iniciá-lo novamente quando quiser.", false);
            SetTrayText(L("Mirage · sem sessão ativa", "Mirage · no active session", "Mirage · sin sesión activa"));
        });
    }

    public void SetLocale(string value) => locale = value is "en" or "es" ? value : "pt-BR";
    private string L(string pt, string en, string es) => locale == "en" ? en : locale == "es" ? es : pt;

    public async Task InitializeBackendAsync()
    {
        ObjectDisposedException.ThrowIf(quitting, this);
        if (initialized) return;
        SetCatalog(await catalog.LoadAsync());
        ObjectDisposedException.ThrowIf(quitting, this);
        selected = games.FirstOrDefault(g => g.Name == "Cyberpunk 2077") ?? games.FirstOrDefault();
        ShowSelection(); initialized = true;
    }

    private void SetCatalog(List<Game> loaded)
    {
        if (quitting) return;
        catalogCount = loaded.Count;
        games = loaded;
        gameById = loaded.DistinctBy(game => game.Id).ToDictionary(game => game.Id);
        featured = Highlights.Select(name => games.FirstOrDefault(game => game.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).OfType<Game>()
            .Concat(games.OrderBy(game => game.Name)).DistinctBy(game => game.Id).ToArray();
        if (selected is not null) { selected = gameById.GetValueOrDefault(selected.Id) ?? selected; ShowSelection(); }
        if (librarySuspended) ReleaseLibraryForBackground();
    }

    public void ReleaseLibraryForBackground()
    {
        if (!initialized || quitting) return;
        librarySuspended = true;
        automaticRefresh.Cancel();
        var needed = prefs.ProcessPlaylist.Select(item => item.GameId).ToHashSet();
        foreach (var game in new[] { selected, requested, active }) if (game is not null) needed.Add(game.Id);
        games = needed.Select(id => gameById.GetValueOrDefault(id)).OfType<Game>().ToList();
        gameById = games.ToDictionary(game => game.Id);
        featured = [];
    }

    public async Task RestoreLibraryAsync()
    {
        ObjectDisposedException.ThrowIf(quitting, this);
        if (!librarySuspended) return;
        var loaded = await catalog.LoadAsync();
        ObjectDisposedException.ThrowIf(quitting, this);
        librarySuspended = false;
        SetCatalog(loaded);
    }

    // The native host owns the sole timer and dispatches all calls on its UI thread.
    public async Task TickAsync()
    {
        if (quitting || PreviewOnly) return;
        if (playlistSession is not null && playlistSession.IsDue(DateTimeOffset.UtcNow) && !busy) await AdvancePlaylistAsync();
    }

    private void ShowSelection()
    {
        if (selected is null) return;
        if (executableGameId != selected.Id) { executableIndex = 0; executableGameId = selected.Id; }
        processOptions = ProcessSession.Options(selected);
        if (executableIndex >= processOptions.Count) executableIndex = 0;
    }

    private void SavePreferences(bool replaceUnreadable = false)
    {
        if (PreviewOnly || quitting) return;
        try { prefs.Save(replaceUnreadable); persistenceWarning = null; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {
            persistenceWarning = prefs.LoadWarning ?? "Não foi possível salvar suas alterações. Elas ficam nesta sessão; tente salvar novamente antes de sair.";
        }
    }
    private void Status(string title, string description, bool success) => status = (title, description);
    private void SetTrayText(string text) { if (!quitting) TrayTextChanged?.Invoke(text[..Math.Min(63, text.Length)]); }
    private void Notify(string title, string text) { if (!quitting && !PreviewOnly && prefs.Notifications) NotificationRaised?.Invoke(title, text); }

    private void AddGameToPlaylist(Game game, int minutes)
    {
        var additions = LibraryTools.PrepareAddition(games, [game.Id], prefs.ProcessPlaylist.Count, minutes, prefs.ProcessWindow, selected?.Id, executableIndex);
        Workspace.Remember("Adicionar jogo");
        prefs.ProcessPlaylist.AddRange(additions);
        prefs.PlaylistDefaultMinutes = minutes; prefs.PlaylistDefaultsVersion = 1;
        SavePreferences();
        Status("Adicionado à lista", $"{game.Name} ficará {FormatMinutes(minutes)}.", true);
    }

    private void PreviewSessionLayout()
    {
        selected = games.FirstOrDefault(g => g.Name == "Resident Evil 4" && g.SupportsPlaylist) ?? selected;
        prefs.ProcessPlaylist = games.Where(g => new[] { "Resident Evil 4", "Cyberpunk 2077", "ELDEN RING" }.Contains(g.Name) && g.SupportsPlaylist).Take(3)
            .Select((g, i) => new ProcessPlaylistItem { GameId = g.Id, GameName = g.Name, Executable = ProcessSession.Options(g)[0].Name, DurationMinutes = 16 + i * 5 }).ToList();
        var game = games.FirstOrDefault(g => g.Name.Equals("ARKNIGHTS: ENDFIELD", StringComparison.OrdinalIgnoreCase) && g.SupportsPlaylist) ?? selected!;
        prefs.ProcessPlaylist.Insert(0, new ProcessPlaylistItem { GameId = game.Id, GameName = game.Name, Executable = ProcessSession.Options(game)[0].Name, DurationMinutes = 16 });
        playlistSession = new ProcessPlaylist(prefs.ProcessPlaylist);
        started = DateTimeOffset.UtcNow.AddMinutes(-5).AddSeconds(-21);
        playlistSession.Start(started);
        selected = active = requested = game; ShowSelection();
    }

    private void RecordRecent(Game target)
    {
        prefs.Recent.Remove(target.Id); prefs.Recent.Insert(0, target.Id); prefs.Recent = prefs.Recent.Take(30).ToList(); SavePreferences();
    }

    private async Task StartProcessAsync()
    {
        if (selected is null || processOptions.Count == 0 || busy) return;
        var target = selected; var executable = processOptions[executableIndex];
        playlistSession = null;
        busy = true; int operation = ++generation; requested = target; active = null;
        Status("Preparando processo…", "Usando o executável registrado para " + target.Name + ".", false);
        try {
            await processSession.StartAsync(target, executable, showWindow: prefs.ProcessWindow, locale: locale);
            if (operation != generation || quitting) { await processSession.StopAsync(); return; }
            started = DateTimeOffset.UtcNow; active = target; RecordRecent(target);
            Status("Processo em execução", "Confira o jogo em Jogos registrados e veja se o contador da missão avança no Discord.", true);
            string title = "Mirage · " + L("processo", "process", "proceso") + " · " + target.Name; SetTrayText(title[..Math.Min(63, title.Length)]);
        } catch (Exception ex) {
            if (operation != generation) return;
            requested = null; active = null; Status("Não foi possível iniciar", ex.Message, false);
        } finally { busy = false; }
    }

    private async Task StartPlaylistAsync()
    {
        if (busy || playlistSession is not null || prefs.ProcessPlaylist.Count == 0) return;
        ProcessPlaylist plan;
        try { plan = new ProcessPlaylist(prefs.ProcessPlaylist); }
        catch (Exception ex) { Status("Lista inválida", ex.Message, false); return; }
        int operation = ++generation;
        playlistSession = plan;
        var item = plan.Start(DateTimeOffset.UtcNow);
        if (!await StartPlaylistItemAsync(item, operation)) playlistSession = null;
    }

    private async Task<bool> StartPlaylistItemAsync(ProcessPlaylistItem item, int operation)
    {
        busy = true; active = null;
        try {
            var game = gameById.GetValueOrDefault(item.GameId) ?? throw new InvalidOperationException($"{item.GameName} não está mais no catálogo.");
            var options = ProcessSession.Options(game);
            var executable = options.FirstOrDefault(e => e.Name.Equals(item.Executable, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"O executável de {item.GameName} não está mais disponível no catálogo.");
            selected = game; executableGameId = game.Id; processOptions = options; executableIndex = options.IndexOf(executable); ShowSelection();
            requested = game;
            if (operation != generation || quitting) return false;
            Status("Iniciando item da lista…", $"{playlistSession!.Index + 1}/{playlistSession.Count} · {game.Name}", false);
            await processSession.StartAsync(game, executable, showWindow: item.ShowWindow, locale: locale);
            if (operation != generation || quitting || playlistSession is null) { await processSession.StopAsync(); return false; }
            started = DateTimeOffset.UtcNow; active = game; RecordRecent(game);
            Status("Lista em andamento", $"{playlistSession.Index + 1}/{playlistSession.Count} · {game.Name} · {FormatMinutes(item.DurationMinutes)}", true);
            string title = $"Mirage · {playlistSession.Index + 1}/{playlistSession.Count} · {game.Name}"; SetTrayText(title[..Math.Min(63, title.Length)]);
            return true;
        } catch (Exception ex) {
            if (operation == generation) { requested = null; active = null; await processSession.StopAsync(); Status("A lista foi interrompida", ex.Message, false); }
            return false;
        } finally { busy = false; }
    }

    private async Task AdvancePlaylistAsync()
    {
        if (playlistSession is null || busy) return;
        int operation = generation;
        var next = playlistSession.Advance(DateTimeOffset.UtcNow);
        if (next is not null) {
            if (!await StartPlaylistItemAsync(next, operation)) { playlistSession = null; }
            return;
        }
        await processSession.StopAsync();
        playlistSession = null; requested = null; active = null; started = default;
        SetTrayText(L("Mirage · lista concluída", "Mirage · list completed", "Mirage · lista completada"));
        Status("Lista concluída", "Todos os processos terminaram no tempo definido.", true);
        Notify(L("Lista concluída", "List completed", "Lista completada"), L("Todos os jogos da lista foram processados.", "All games in the list have been processed.", "Todos los juegos de la lista se han procesado."));
    }

    private async Task StopAsync()
    {
        generation++; requested = null; active = null; playlistSession = null;
        await processSession.StopAsync();
        started = default; SetTrayText(L("Mirage · sem processo ativo", "Mirage · no active process", "Mirage · sin proceso activo"));
        Status("Sessão encerrada", "Escolha outro jogo quando quiser.", false);
    }

    private static string FormatMinutes(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}min" : $"{minutes} min";

    public void Dispose()
    {
        if (quitting) return;
        quitting = true; generation++;
        automaticRefresh.Cancel(); automaticRefresh.Dispose();
        requested = active = null; playlistSession = null;
        processSession.Dispose();
        TrayTextChanged = null; NotificationRaised = null;
    }
}
