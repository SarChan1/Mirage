using System.Text.Json;
using Mirage;

internal static class PreferencesTests
{
    public static void Run()
    {
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
        var root = Path.Combine(Path.GetTempPath(), "Mirage.Tests", "preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "preferences.json");
        try {
            var fresh = Preferences.Load(root);
            Check(fresh.PlaylistDefaultMinutes == 16 && fresh.LoadWarning is null, "first launch uses defaults without a false storage warning");
            fresh.Favorites.Add("123456789012345678"); fresh.PlaylistDefaultMinutes = 24; fresh.Save();
            var loaded = Preferences.Load(root);
            Check(loaded.Favorites.SetEquals(fresh.Favorites) && loaded.PlaylistDefaultMinutes == 24, "preferences persist through a real file round trip");
            Check(!File.Exists(file + ".tmp") && Directory.GetFiles(root).Length == 1, "successful saves do not accumulate temporary files or backups");
            var original = File.ReadAllBytes(file);
            loaded.PlaylistDefaultMinutes = 29;
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                try { loaded.Save(); Check(false, "locked destination rejects replacement"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Check(true, "locked destination reports a write failure"); }
            }
            Check(File.ReadAllBytes(file).SequenceEqual(original) && !File.Exists(file + ".tmp"), "a failed save leaves the original file unchanged and removes its temporary file");
            loaded.Save();
            Check(Preferences.Load(root).PlaylistDefaultMinutes == 29, "saving can be retried after a file lock is released");
            const string broken = "{ incomplete preferences";
            File.WriteAllText(file, broken);
            var corrupt = Preferences.Load(root);
            Check(corrupt.LoadWarning is not null && corrupt.RequiresStorageReplacement && File.ReadAllText(file) == broken, "corrupt preferences are reported and preserved");
            try { corrupt.Save(); Check(false, "corrupt preferences block implicit overwrite"); }
            catch (IOException) { Check(File.ReadAllText(file) == broken, "ordinary actions cannot replace unreadable preferences"); }
            using (var controller = new MirageController(false, action => action(), corrupt)) {
                controller.InitializeBackendAsync().GetAwaiter().GetResult();
                controller.DispatchWebAsync("configure", JsonSerializer.SerializeToElement(new { minutes = 23 })).GetAwaiter().GetResult();
                var state = JsonSerializer.SerializeToElement(controller.GetWebState(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Check(state.GetProperty("storage").GetProperty("warning").ValueKind == JsonValueKind.String && state.GetProperty("storage").GetProperty("replacementRequired").GetBoolean(), "controller exposes failed persistence independently from session status");
                controller.DispatchWebAsync("retrySave", JsonSerializer.SerializeToElement(new { })).GetAwaiter().GetResult();
                Check(File.ReadAllText(file) == broken, "retry cannot implicitly discard an unreadable preferences file");
                controller.DispatchWebAsync("replaceUnreadablePreferences", JsonSerializer.SerializeToElement(new { })).GetAwaiter().GetResult();
                state = JsonSerializer.SerializeToElement(controller.GetWebState(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Check(state.GetProperty("storage").GetProperty("warning").ValueKind == JsonValueKind.Null && Preferences.Load(root).PlaylistDefaultMinutes == 23, "explicit recovery clears the warning only after the controller saves successfully");
            }
            File.WriteAllText(file, broken); corrupt = Preferences.Load(root);
            corrupt.PlaylistDefaultMinutes = 37; corrupt.Save(replaceUnreadable: true);
            Check(Preferences.Load(root).PlaylistDefaultMinutes == 37 && !corrupt.RequiresStorageReplacement && corrupt.LoadWarning is null, "explicit replacement restores saving without retaining backup copies");
            File.WriteAllText(file, "null");
            Check(Preferences.Load(root).RequiresStorageReplacement, "a null JSON document is treated as unreadable data");
            File.WriteAllText(file, "{\"Favorites\":[null,\"bad\",\"123456789012345678\"],\"Recent\":[null,\"bad\",\"123456789012345678\",\"123456789012345678\"],\"ProcessPlaylist\":[null],\"SavedPlaylists\":null,\"PlaylistDefaultMinutes\":-5}");
            var normalized = Preferences.Load(root);
            Check(normalized.LoadWarning is null && normalized.Favorites.Count == 1 && normalized.Recent.Count == 1 && normalized.ProcessPlaylist.Count == 0 && normalized.SavedPlaylists.Count == 0 && normalized.PlaylistDefaultMinutes == 1, "malformed optional fields are normalized without losing valid favorites");
            var item = new ProcessPlaylistItem { GameId = "123456789012345678", GameName = "Example", Executable = "example.exe" };
            normalized.ProcessPlaylist = [item, item.Copy()];
            normalized.Save();
            Check(Preferences.Load(root).ProcessPlaylist.Count == 1, "duplicate queue identities cannot survive loading");
            File.WriteAllText(file + ".tmp", "interrupted old write");
            normalized.Save();
            Check(!File.Exists(file + ".tmp") && Preferences.Load(root).ProcessPlaylist.Count == 1, "a leftover interrupted-write file cannot replace valid saved data");
            var serialized = JsonSerializer.Serialize(normalized);
            Check(!serialized.Contains("LoadWarning") && !serialized.Contains("RequiresStorageReplacement") && !serialized.Contains(root), "diagnostics and storage paths are excluded from preferences JSON");
            using (var oversized = new FileStream(file, FileMode.Create)) oversized.SetLength(16 * 1024 * 1024 + 1);
            Check(Preferences.Load(root).RequiresStorageReplacement, "oversized preferences are rejected before allocating their contents");
        } finally {
            var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mirage.Tests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(root, recursive: true);
        }
        Console.WriteLine("PREFERENCES_TESTS_OK " + passed);
    }
}
