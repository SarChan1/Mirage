using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage;
public sealed class CatalogService
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    public const string Endpoint = "https://discord.com/api/v9/applications/detectable";
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static CatalogService()
    {
        string version = typeof(CatalogService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mirage/" + version);
    }
    public async Task<List<Game>> LoadAsync()
    {
        string cache = Path.Combine(Paths.Data, "catalog.json");
        try {
            if (File.Exists(cache)) {
                var games = JsonSerializer.Deserialize<List<Game>>(await File.ReadAllTextAsync(cache));
                if (games is { Count: > 100 } && games.Any(g => g.Executables is { Length: > 0 })) { GameSearch.Prepare(games); return games; }
            }
        } catch { }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mirage.catalog.json")!;
        var bundled = await JsonSerializer.DeserializeAsync<List<Game>>(stream) ?? [];
        GameSearch.Prepare(bundled); return bundled;
    }
    public async Task<List<Game>> RefreshAsync(CancellationToken ct = default)
    {
        await refreshGate.WaitAsync(ct);
        try { return await RefreshCoreAsync(ct); }
        finally { refreshGate.Release(); }
    }
    private async Task<List<Game>> RefreshCoreAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(35)); ct = timeout.Token;
        using var response = await Http.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 40_000_000) throw new IOException("Catálogo maior que o esperado.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(source, cancellationToken: ct);
        var games = new List<Game>();
        foreach (var item in doc.RootElement.EnumerateArray()) {
            var id = item.GetProperty("id").GetString(); var name = item.GetProperty("name").GetString();
            if (id is null || !id.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(name)) continue;
            var g = new Game { Id = id, Name = name };
            if (item.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array) g.Aliases = aliases.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToArray();
            if (item.TryGetProperty("icon_hash", out var icon) && icon.ValueKind == JsonValueKind.String) g.Icon = icon.GetString();
            if (item.TryGetProperty("cover_image_hash", out var cover) && cover.ValueKind == JsonValueKind.String) g.Cover = cover.GetString();
            if (item.TryGetProperty("executables", out var executables) && executables.ValueKind == JsonValueKind.Array)
                g.Executables = executables.EnumerateArray().Where(e => e.TryGetProperty("name", out var en) && en.ValueKind == JsonValueKind.String && e.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.String)
                    .Select(e => new GameExecutable { Name = e.GetProperty("name").GetString()!, Os = e.GetProperty("os").GetString()!, IsLauncher = e.TryGetProperty("is_launcher", out var launcher) && launcher.ValueKind == JsonValueKind.True }).ToArray();
            if (item.TryGetProperty("third_party_skus", out var skus) && skus.ValueKind == JsonValueKind.Array)
                foreach (var sku in skus.EnumerateArray()) if (sku.TryGetProperty("distributor", out var d) && d.GetString() == "steam" && sku.TryGetProperty("id", out var sid)) { g.SteamId = sid.ValueKind == JsonValueKind.String ? sid.GetString() : sid.GetRawText(); break; }
            games.Add(g);
        }
        games = games.DistinctBy(g => g.Id).ToList();
        if (games.Count < 100) throw new IOException("A resposta não contém um catálogo válido.");
        Directory.CreateDirectory(Paths.Data);
        string cache = Path.Combine(Paths.Data, "catalog.json");
        await File.WriteAllTextAsync(cache + ".tmp", JsonSerializer.Serialize(games), ct); File.Move(cache + ".tmp", cache, true);
        GameSearch.Prepare(games); return games;
    }
}
