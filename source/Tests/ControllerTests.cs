using System.Text.Json;
using Mirage;

// Runs without PresentationFramework, a native window, RPC or process activity.
internal static class ControllerTests
{
    public static async Task RunAsync()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS " + name); passed++; }
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonElement Data(object value) => JsonSerializer.SerializeToElement(value, json);
        var prefs = new Preferences();
        var previousVersionPreferences = JsonSerializer.Deserialize<Preferences>("{\"PlaylistDefaultMinutes\":16}")!;
        Check(previousVersionPreferences.Notifications && previousVersionPreferences.MinimizeToTray, "older preferences retain existing notification and tray defaults");
        using var controller = new MirageController(true, action => action(), prefs);
        async Task<JsonElement> Send(string command, object? args = null) => Data(await controller.DispatchWebAsync(command, Data(args ?? new { })));
        JsonElement State() => Data(controller.GetWebState());
        async Task Reject(string command, object? args, string message) {
            try { await Send(command, args); Check(false, message); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Check(true, message); }
        }
        await controller.InitializeBackendAsync();
        var boot = await Send("boot");
        Check(boot.GetProperty("state").GetProperty("catalogCount").GetInt32() > 20000 && boot.GetProperty("results").GetProperty("items").GetArrayLength() == 60, "controller loads catalog and first page without a WPF window");
        Check(!typeof(MirageController).Assembly.GetReferencedAssemblies().Any(a => a.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase"), "controller tests compile without any WPF assembly reference");
        var results = await Send("search", new { query = "Resident Evil", onlySequence = true, limit = 3 });
        var game = results.GetProperty("items")[0]; string gameId = game.GetProperty("id").GetString()!;
        await Send("select", new { gameId });
        await controller.InitializeBackendAsync();
        Check(State().GetProperty("selected").GetProperty("id").GetString() == gameId, "initialization is idempotent and retains selection");
        await Send("configure", new { minutes = 23, showWindow = false });
        var configured = State().GetProperty("preferences");
        Check(configured.GetProperty("minutes").GetInt32() == 23 && !configured.GetProperty("showWindow").GetBoolean(), "process preferences cross the native bridge");
        await Send("configure", new { notifications = false, minimizeToTray = false });
        Check(!prefs.Notifications && !prefs.MinimizeToTray && !State().GetProperty("preferences").GetProperty("notifications").GetBoolean(), "notification and tray settings cross the native bridge");
        await Reject("configure", new { minutes = 44, notifications = "yes" }, "invalid checkbox values are rejected");
        Check(prefs.PlaylistDefaultMinutes == 23 && !prefs.Notifications, "invalid combined settings do not partially overwrite preferences");
        await Send("favorite", new { gameId });
        Check((await Send("search", new { collection = "favorites" })).GetProperty("items").GetArrayLength() == 1, "favorite commands update the filtered catalog");
        int variant = game.GetProperty("executables").GetArrayLength() - 1;
        await Send("variant", new { index = variant });
        await Send("add", new { gameId, minutes = 23 });
        Check(prefs.ProcessPlaylist.Count == 1 && prefs.ProcessPlaylist[0].DurationMinutes == 23 && !prefs.ProcessPlaylist[0].ShowWindow && prefs.ProcessPlaylist[0].Executable == game.GetProperty("executables")[variant].GetString(), "queue addition preserves duration, auxiliary window and chosen executable");
        Check(State().GetProperty("status").GetProperty("title").GetString() == "Adicionado à lista", "status is data independent of WPF labels");
        await Send("undoPlaylist");
        Check(prefs.ProcessPlaylist.Count == 0, "undo restores queue before native controller addition");
        await Send("add", new { gameId });
        Check(prefs.ProcessPlaylist.Single().DurationMinutes == 16, "omitted queue duration defaults to 16 minutes");
        await Send("savePlaylist", new { name = "Teste isolado" });
        await Send("clear");
        await Send("usePlaylist", new { savedId = prefs.SavedPlaylists[0].Id, merge = "replace" });
        Check(prefs.ProcessPlaylist.Count == 1 && prefs.SavedPlaylists.Count == 1, "saved sequences remain usable through the native bridge");
        await Reject("add", new { gameId, minutes = 0 }, "invalid duration is rejected before changing the queue");
        Check(prefs.ProcessPlaylist.Count == 1, "failed command leaves the queue intact");
        await Reject("activate", null, "preview cannot start a real process");
        await Reject("startPlaylist", null, "preview cannot start real processes");
        await Send("previewSession");
        var session = State().GetProperty("session");
        Check(session.GetProperty("isPlaylist").GetBoolean() && session.GetProperty("confirmed").GetBoolean() && session.GetProperty("count").GetInt32() == 4, "running queue fixture uses controller state alone");
        await Reject("clear", null, "running queue remains protected from edits");
        await controller.TickAsync();
        Check(State().GetProperty("session").GetProperty("game").GetProperty("id").GetString() == session.GetProperty("game").GetProperty("id").GetString(), "preview timer never starts or advances real sessions");
        int catalogCount = State().GetProperty("catalogCount").GetInt32();
        controller.ReleaseLibraryForBackground();
        Check(controller.IsLibrarySuspended && controller.LoadedGameCount <= 6, "background mode releases the full catalog but retains queued games");
        Check(State().GetProperty("catalogCount").GetInt32() == catalogCount && State().GetProperty("session").GetProperty("game").GetProperty("id").GetString() == session.GetProperty("game").GetProperty("id").GetString(), "background catalog release preserves public count and active session");
        await controller.RestoreLibraryAsync();
        Check(!controller.IsLibrarySuspended && controller.LoadedGameCount == catalogCount, "restoring reloads the entire local catalog");
        Check((await Send("search", new { query = "Cyberpunk" })).GetProperty("items").GetArrayLength() > 0, "restored catalog supports full search again");
        await Send("stop");
        Check(State().GetProperty("session").ValueKind == JsonValueKind.Null, "stopping clears the session in the controller");
        controller.Dispose(); controller.Dispose();
        controller.ReleaseLibraryForBackground();
        await controller.TickAsync();
        try { await Send("configure", new { minutes = 99 }); Check(false, "disposed controller rejects commands"); }
        catch (ObjectDisposedException) { Check(true, "disposed controller rejects commands and tolerates repeated disposal"); }
        Console.WriteLine($"CONTROLLER_TESTS_OK {passed}");
    }
}
