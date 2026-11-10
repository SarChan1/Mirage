using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage;
public sealed class ProcessSession : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? worker;
    private EventWaitHandle? stop;
    private bool disposed;
    public string? ExecutablePath { get; private set; }
    public int? ProcessId { get { try { return worker is { HasExited: false } ? worker.Id : null; } catch { return null; } } }
    public bool Running => ProcessId is not null;
    public event Action? Exited;
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 220 || Path.IsPathRooted(path)) throw new ArgumentException("Caminho de executável inválido no catálogo.");
        var parts = path.Replace('\\', '/').Split('/');
        foreach (var part in parts) {
            if (string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Reserved.Contains(part.Split('.')[0]))
                throw new ArgumentException("Caminho de executável inválido no catálogo.");
        }
        if (!parts[^1].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Este registro não contém um executável Windows.");
        return Path.Combine(parts);
    }
    public static List<GameExecutable> Options(Game game) => game.CachedExecutables ?? (game.Executables ?? []).Where(e => e.Os == "win32" && !e.IsLauncher && IsSupported(e.Name)).DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    private static bool IsSupported(string name) { try { ValidateRelativePath(name); return true; } catch (ArgumentException) { return false; } }
    public static string ResolvePath(string root, string gameId, string version, string relative)
    {
        if (gameId.Length is < 10 or > 22 || !gameId.All(char.IsAsciiDigit) || version.Length != 16 || !version.All(char.IsAsciiHexDigit)) throw new ArgumentException("Identificador de processo inválido.");
        string basePath = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(basePath, gameId, version, ValidateRelativePath(relative)));
        if (!path.StartsWith(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("O caminho saiu da pasta do Mirage.");
        return path;
    }
    private static void PrepareDirectory(string directory)
    {
        string full = Path.GetFullPath(directory);
        string volume = Path.GetPathRoot(full)!;
        string current = volume;
        foreach (var part in full[volume.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("A pasta de processos contém um redirecionamento. Use uma pasta local normal.");
            Directory.CreateDirectory(current);
        }
    }
    public async Task StartAsync(Game game, GameExecutable executable, string? testRoot = null, bool showWindow = false, string locale = "pt-BR")
    {
        if (!Options(game).Any(e => e.Name == executable.Name)) throw new ArgumentException("Escolha um executável Windows disponível para este jogo.");
        await gate.WaitAsync();
        try {
            ObjectDisposedException.ThrowIf(disposed, this);
            await StopCoreAsync();
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mirage.ProcessWorker.exe") ?? throw new IOException("O auxiliar do Mirage não está disponível.");
            using var memory = new MemoryStream(); await resource.CopyToAsync(memory); byte[] bytes = memory.ToArray();
            string version = Convert.ToHexString(SHA256.HashData(bytes))[..16];
            string path = ResolvePath(testRoot ?? Path.Combine(Paths.Data, "processes"), game.Id, version, executable.Name);
            await Task.Run(() => {
                PrepareDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("O executável auxiliar contém um redirecionamento.");
                if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                else if (!SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes))) throw new IOException("O auxiliar local foi alterado. Não foi iniciado.");
            });
            string eventName = @"Local\Mirage.Process." + Guid.NewGuid().ToString("N");
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            using var parent = Process.GetCurrentProcess();
            var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)! };
            info.ArgumentList.Add(parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add(eventName);
            info.ArgumentList.Add(showWindow ? "window" : "background");
            info.ArgumentList.Add(locale is "en" or "es" ? locale : "pt-BR");
            var process = Process.Start(info) ?? throw new IOException("Não foi possível iniciar o processo auxiliar.");
            worker = process; ExecutablePath = path;
            await Task.Delay(500);
            if (process.HasExited) throw new IOException("O processo auxiliar encerrou antes de iniciar. Código: " + process.ExitCode + ".");
            process.Exited += (_, _) => { if (ReferenceEquals(worker, process)) Exited?.Invoke(); };
            process.EnableRaisingEvents = true;
        } catch { await StopCoreAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { await StopCoreAsync(); } finally { gate.Release(); }
    }
    private async Task StopCoreAsync()
    {
        var previous = worker; worker = null;
        try { stop?.Set(); } catch (ObjectDisposedException) { }
        if (previous is not null) {
            try { await previous.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { if (!previous.HasExited) { previous.Kill(); await previous.WaitForExitAsync(); } }
            finally { previous.Dispose(); }
        }
        stop?.Dispose(); stop = null; ExecutablePath = null;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { stop?.Set(); } catch { }
        worker?.Dispose(); worker = null; stop?.Dispose(); stop = null;
    }
}
