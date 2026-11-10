using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Mirage;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        bool webSmoke = e.Args.Length >= 2 && e.Args[0].StartsWith("--web-smoke", StringComparison.Ordinal);
        bool preview = webSmoke;
        mutex = new Mutex(true, preview ? @"Local\Mirage.Preview." + Guid.NewGuid() : @"Local\Mirage.Desktop.1", out bool created);
        if (!created) { MessageBox.Show("O Mirage já está aberto. Procure o ícone perto do relógio do Windows.", "Mirage"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => {
            try { Directory.CreateDirectory(Paths.Data); File.AppendAllText(Path.Combine(Paths.Data, "errors.log"), DateTime.Now + " " + args.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("Não foi possível concluir essa ação. Tente novamente. O diagnóstico foi salvo em %LOCALAPPDATA%\\Mirage\\errors.log.", "Mirage");
            args.Handled = true;
        };
        var webWindow = new WebWindow(webSmoke);
        MainWindow = webWindow; webWindow.Show();
        if (webSmoke) {
            try {
                if (e.Args[0].Contains("small", StringComparison.Ordinal)) { webWindow.Width = 1060; webWindow.Height = 720; }
                if (e.Args[0].Contains("wide", StringComparison.Ordinal)) { webWindow.Width = 1920; webWindow.Height = 1080; }
                await webWindow.RunSmokeAsync(e.Args[1], e.Args[0].Contains("save-center-en", StringComparison.Ordinal) ? "save-center-en" : e.Args[0].Contains("save-center-es", StringComparison.Ordinal) ? "save-center-es" : e.Args[0].Contains("locale-en", StringComparison.Ordinal) ? "locale-en" : e.Args[0].Contains("locale-es", StringComparison.Ordinal) ? "locale-es" : e.Args[0].Contains("locale-pt", StringComparison.Ordinal) ? "locale-pt" : e.Args[0].Contains("save-center", StringComparison.Ordinal) ? "save-center" : e.Args[0].Contains("endurance", StringComparison.Ordinal) ? "endurance" : e.Args[0].Contains("recovery", StringComparison.Ordinal) ? "recovery" : e.Args[0].Contains("background", StringComparison.Ordinal) ? "background" : e.Args[0].Contains("running", StringComparison.Ordinal) ? "running" : e.Args[0].Contains("-saves", StringComparison.Ordinal) ? "saves" : e.Args[0].Contains("saved", StringComparison.Ordinal) ? "saved" : e.Args[0].Contains("batch", StringComparison.Ordinal) ? "batch" : e.Args[0].Contains("queue", StringComparison.Ordinal) ? "queue" : "library");
            } catch (Exception ex) {
                try { File.WriteAllText(Path.ChangeExtension(e.Args[1], ".failure.txt"), ex.ToString()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Shutdown(1);
            }
        }
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
