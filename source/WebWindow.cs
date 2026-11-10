using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace Mirage;

public sealed partial class WebWindow : Window
{
    private const string Origin = "https://mirage.local";
    private readonly MirageController engine;
    private readonly Forms.NotifyIcon tray = new();
    private WebView2? view;
    private WebView2 browser => view ?? throw new InvalidOperationException("A interface está em repouso.");
    private Task? uiInitialization;
    private Task? backendInitialization;
    private bool uiReady, libraryRefreshStarted;
    private readonly DispatcherTimer updates = new() { Interval = ForegroundTickInterval };
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private bool closing;
    private int backendTicks;
    private string locale = "pt-BR";
    private string? lastPostedState;
    public Task Ready => ready.Task;
    public bool PreviewOnly { get; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    public WebWindow(bool preview)
    {
        PreviewOnly = preview;
        Title = "Mirage"; Width = 1400; Height = 900; MinWidth = 1060; MinHeight = 700;
        WindowStyle = WindowStyle.None; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(15, 15, 20));
        var icon = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/mirage.ico"));
        if (icon is not null) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(icon.Stream);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(10) });
        engine = new MirageController(preview, action => Dispatcher.BeginInvoke(action));
        ConfigureTray();
        SourceInitialized += (_, _) => { int enabled = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int)); };
        Loaded += async (_, _) => await EnsureUiAsync();
        IsVisibleChanged += (_, _) => UpdateVisibility();
        StateChanged += (_, _) => { if (!PreviewOnly && engine.HideWebOnMinimize && WindowState == WindowState.Minimized) Hide(); UpdateVisibility(); };
        Closing += CloseHost;
        Closed += (_, _) => { backgroundRelease?.Cancel(); updates.Stop(); engine.Dispose(); tray.Visible = false; tray.Icon?.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); view?.Dispose(); view = null; };
        updates.Tick += async (_, _) => {
            if (closing) return;
            backendTicks++;
            await engine.TickAsync();
            if (!closing && UiVisible && uiReady && view?.CoreWebView2 is not null && (engine.HasWebSession || backendTicks % 4 == 0)) PublishState();
            if (!UiVisible && !engine.HasWebSession) updates.Stop();
        };
    }
    private void ConfigureTray()
    {
        if (PreviewOnly) return;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Mirage", null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add("Encerrar sessão", null, async (_, _) => {
            try { await Dispatcher.InvokeAsync(() => engine.DispatchWebAsync("stop", JsonSerializer.SerializeToElement(new { }))).Task.Unwrap(); }
            catch (InvalidOperationException ex) { tray.ShowBalloonTip(2500, "Mirage", ex.Message, Forms.ToolTipIcon.Info); }
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair do Mirage", null, (_, _) => Dispatcher.Invoke(() => { closing = true; Close(); }));
        tray.ContextMenuStrip = menu; tray.Text = "Mirage · sem processo ativo";
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/mirage.ico"));
        if (resource is not null) { using (resource.Stream) tray.Icon = new System.Drawing.Icon(resource.Stream); }
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
        engine.TrayTextChanged += text => tray.Text = text;
        engine.NotificationRaised += (title, text) => tray.ShowBalloonTip(3500, title, text, Forms.ToolTipIcon.Info);
        tray.Visible = true;
    }
    private string L(string pt, string en, string es) => locale == "en" ? en : locale == "es" ? es : pt;
    private void ApplyLocale(string value)
    {
        locale = value is "en" or "es" ? value : "pt-BR";
        engine.SetLocale(locale);
        if (tray.ContextMenuStrip is { } menu && menu.Items.Count >= 4) {
            menu.Items[0].Text = L("Abrir Mirage", "Open Mirage", "Abrir Mirage");
            menu.Items[1].Text = L("Encerrar sessão", "Stop session", "Detener sesión");
            menu.Items[3].Text = L("Sair do Mirage", "Exit Mirage", "Salir de Mirage");
        }
        if (!engine.HasWebSession) tray.Text = L("Mirage · sem processo ativo", "Mirage · no active process", "Mirage · sin proceso activo");
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    private Task EnsureUiAsync()
    {
        if (closing) return Task.CompletedTask;
        return uiInitialization ??= InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        try {
            await (backendInitialization ??= engine.InitializeBackendAsync());
            await engine.RestoreLibraryAsync();
            if (closing) return;
            var current = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 15, 15, 20) };
            view = current; Content = current;
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Paths.Data, PreviewOnly ? "WebView2-preview" : "WebView2"));
            await current.EnsureCoreWebView2Async(environment);
            if (closing) return;
            var core = current.CoreWebView2;
            core.ProcessFailed += (_, e) => {
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    _ = Dispatcher.BeginInvoke(new Action(() => ShowUiFailure(current, "O componente que exibe a janela parou de responder.")));
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__MIRAGE_VIEW_STATE__ = " + (uiSnapshot ?? "null") + ";");
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = PreviewOnly;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = true;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NavigationStarting += (_, e) => { if (!IsAppOrigin(e.Uri)) e.Cancel = true; };
            core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += ServeResource;
            core.WebMessageReceived += Receive;
            core.NavigationCompleted += (_, e) => {
                if (!ReferenceEquals(view, current) || closing) return;
                if (!e.IsSuccess) {
                    ready.TrySetException(new IOException("Não foi possível carregar a interface local: " + e.WebErrorStatus));
                    if (!PreviewOnly) _ = Dispatcher.BeginInvoke(new Action(() => ShowUiFailure(current, "Não foi possível carregar a interface local.")));
                }
                else {
                    recoveryVisible = false; uiReady = true; updates.Start(); ready.TrySetResult();
                    if (!libraryRefreshStarted) { libraryRefreshStarted = true; _ = engine.UpdateLibraryInformationAsync(); }
                    if (!UiVisible) UpdateVisibility();
                }
            };
            core.Navigate(Origin + "/index.html");
        } catch (Exception ex) {
            ready.TrySetException(ex);
            if (!PreviewOnly && !closing) _ = Dispatcher.BeginInvoke(new Action(() => ShowUiFailure(view, "Não foi possível abrir a interface. Confira se o Microsoft WebView2 está instalado.")));
        }
    }
    private static bool IsAppOrigin(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "mirage.local" && uri.IsDefaultPort;
    private void ServeResource(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!IsAppOrigin(e.Request.Uri)) return;
        string path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath).TrimStart('/');
        if (string.IsNullOrEmpty(path)) path = "index.html";
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')) { e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
        var assembly = Assembly.GetExecutingAssembly();
        string resource = "Mirage.Web/" + path;
        string? manifestName = assembly.GetManifestResourceNames().FirstOrDefault(name => name.Replace('\\', '/') == resource);
        var stream = manifestName is null ? null : assembly.GetManifestResourceStream(manifestName);
        if (stream is null) { e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
        string type = Path.GetExtension(path).ToLowerInvariant() switch {
            ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8", ".svg" => "image/svg+xml", ".woff2" => "font/woff2", ".woff" => "font/woff", ".png" => "image/png", ".ico" => "image/x-icon", _ => "application/octet-stream"
        };
        string headers = "Content-Type: " + type + "\r\nCache-Control: no-cache\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' https://cdn.discordapp.com https://shared.akamai.steamstatic.com https://cdn.cloudflare.steamstatic.com data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'";
        e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", headers);
    }
    private void Post(object value)
    {
        if (closing || view?.CoreWebView2 is null) return;
        try { view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, json)); } catch (InvalidOperationException) { }
    }
    private void PublishState()
    {
        string data = JsonSerializer.Serialize(new { type = "state", state = engine.GetWebState() }, json);
        if (data == lastPostedState) return;
        lastPostedState = data;
        view?.CoreWebView2?.PostWebMessageAsJson(data);
    }
    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsAppOrigin(e.Source)) return;
        string? id = null;
        try {
            if (e.WebMessageAsJson.Length > 65536) throw new ArgumentException("Mensagem maior que o permitido.");
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var message = document.RootElement;
            id = message.GetProperty("id").GetString();
            if (!Guid.TryParse(id, out _)) throw new ArgumentException("Identificador inválido.");
            string command = message.GetProperty("command").GetString() ?? "";
            var args = message.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object ? payload : JsonSerializer.SerializeToElement(new { });
            object result;
            switch (command) {
                case "locale": ApplyLocale(args.TryGetProperty("locale", out var localeValue) ? localeValue.GetString() ?? "pt-BR" : "pt-BR"); result = new { ok = true }; break;
                case "saves.export": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não cria backups.");
                    bool zip = args.TryGetProperty("format", out var format) && format.GetString() == "zip";
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("Escolha onde salvar o backup dos saves", "Choose where to save the save backup", "Elige dónde guardar la copia de las partidas"), Multiselect = false };
                    if (dialog.ShowDialog(this) != true) { result = new SaveExportResult(false, null, 0, 0, 0, zip ? "zip" : "folder"); break; }
                    result = await engine.ExportSaveLibraryAsync(dialog.FolderName, zip); break;
                }
                case "saves.openExport": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não abre pastas do computador.");
                    string output = engine.ResolveLastSaveExport();
                    string folder = File.Exists(output) ? Path.GetDirectoryName(output)! : output;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
                    result = new { ok = true }; break;
                }
                case "saves.restore.chooseZip": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não abre arquivos do computador.");
                    var dialog = new Microsoft.Win32.OpenFileDialog { Title = L("Escolha um backup de saves do Mirage", "Choose a Mirage save backup", "Elige una copia de partidas de Mirage"), Filter = "Mirage ZIP (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
                    if (dialog.ShowDialog(this) != true) { result = new { selected = false }; break; }
                    result = await engine.AnalyzeSaveRestoreAsync(dialog.FileName); break;
                }
                case "saves.restore.chooseFolder": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não abre pastas do computador.");
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("Escolha uma pasta de backup do Mirage", "Choose a Mirage backup folder", "Elige una carpeta de copia de Mirage"), Multiselect = false };
                    if (dialog.ShowDialog(this) != true) { result = new { selected = false }; break; }
                    result = await engine.AnalyzeSaveRestoreAsync(dialog.FolderName); break;
                }
                case "saves.restore.open": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não abre pastas do computador.");
                    string output = engine.ResolveLastRestorePath(args.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() ?? "pending" : "pending");
                    string folder = File.Exists(output) ? Path.GetDirectoryName(output)! : output;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
                    result = new { ok = true }; break;
                }
                case "saves.copy": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não altera a área de transferência.");
                    string path = engine.GetSavePathToCopy(args.GetProperty("id").GetString() ?? "");
                    try { System.Windows.Clipboard.SetText(path); }
                    catch (ExternalException) { throw new InvalidOperationException("Não foi possível copiar agora. Tente novamente."); }
                    result = new { ok = true }; break;
                }
                case "saves.chooseFolder": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não abre o seletor de pastas.");
                    string gameId = args.GetProperty("gameId").GetString() ?? "";
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("Selecione a pasta de instalação do jogo", "Select the game installation folder", "Selecciona la carpeta de instalación del juego"), Multiselect = false };
                    bool chosen = dialog.ShowDialog(this) == true;
                    if (chosen) engine.SetSaveInstallFolder(gameId, dialog.FolderName);
                    result = new { selected = chosen }; break;
                }
                case "saves.open": {
                    if (PreviewOnly) throw new InvalidOperationException("A prévia não abre pastas do computador.");
                    var location = engine.ResolveSaveLocation(args.GetProperty("id").GetString() ?? "");
                    var explorer = new System.Diagnostics.ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), UseShellExecute = true };
                    explorer.ArgumentList.Add(location.Kind == "file" ? Path.GetDirectoryName(location.Path)! : location.Path);
                    System.Diagnostics.Process.Start(explorer);
                    result = new { ok = true }; break;
                }
                case "window.minimize": WindowState = WindowState.Minimized; result = new { ok = true }; break;
                case "window.maximize": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; result = new { ok = true }; break;
                case "window.close": result = new { ok = true }; _ = Dispatcher.BeginInvoke(new Action(Close)); break;
                default: result = await engine.DispatchWebAsync(command, args); break;
            }
            Post(new { id, result });
        } catch (Exception ex) { Post(new { id, error = ex.Message }); }
    }
    private void CloseHost(object? sender, CancelEventArgs e)
    {
        if (!closing && engine.HasWebSession && !PreviewOnly) { e.Cancel = true; Hide(); return; }
        if (closing) return;
        closing = true;
        engine.Dispose();
    }
    public async Task CaptureAsync(string path)
    {
        using var file = File.Create(path);
        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
    }
    public async Task RunSmokeAsync(string path, string scene = "library")
    {
        await Ready.WaitAsync(TimeSpan.FromSeconds(45));
        for (int i = 0; i < 200; i++) {
            if (await browser.ExecuteScriptAsync("Boolean(window.__MIRAGE_READY__)") == "true") break;
            await Task.Delay(100);
        }
        await browser.ExecuteScriptAsync("window.__MIRAGE_SMOKE__(" + JsonSerializer.Serialize(scene) + ").then(report => window.__MIRAGE_REPORT__ = report).catch(error => window.__MIRAGE_REPORT__ = { ok: false, error: String(error), stack: error.stack })");
        string report = "null";
        for (int i = 0; i < 300; i++) {
            report = await browser.ExecuteScriptAsync("window.__MIRAGE_REPORT__ || null");
            if (report != "null") break;
            await Task.Delay(100);
        }
        File.WriteAllText(Path.ChangeExtension(path, ".json"), report);
        if (!report.Contains("\"ok\":true", StringComparison.Ordinal)) { await CaptureAsync(Path.ChangeExtension(path, ".failure.png")); throw new InvalidOperationException("A verificação da interface falhou: " + report); }
        if (scene == "queue") await browser.ExecuteScriptAsync("document.querySelector('[data-nav=playlist]').click()");
        if (scene == "running") {
            await browser.ExecuteScriptAsync("window.__MIRAGE_RUNTIME_SMOKE__().then(report => window.__MIRAGE_RUNTIME_REPORT__ = report).catch(error => window.__MIRAGE_RUNTIME_REPORT__ = { ok: false, error: String(error) })");
            string runtime = "null";
            for (int i = 0; i < 200; i++) { runtime = await browser.ExecuteScriptAsync("window.__MIRAGE_RUNTIME_REPORT__ || null"); if (runtime != "null") break; await Task.Delay(100); }
            if (!runtime.Contains("\"ok\":true", StringComparison.Ordinal)) throw new InvalidOperationException("A verificação da sessão falhou: " + runtime);
            report = await browser.ExecuteScriptAsync("({...window.__MIRAGE_REPORT__, runtime:window.__MIRAGE_RUNTIME_REPORT__})");
            File.WriteAllText(Path.ChangeExtension(path, ".json"), report);
        }
        if (scene is "background" or "endurance") {
            var result = await RunBackgroundChecksAsync(extended: scene == "endurance");
            var merged = JsonNode.Parse(report)!.AsObject();
            merged["background"] = JsonSerializer.SerializeToNode(result, json);
            report = merged.ToJsonString();
        }
        if (scene == "recovery") {
            var merged = JsonNode.Parse(report)!.AsObject();
            merged["recovery"] = JsonSerializer.SerializeToNode(await RunRecoveryChecksAsync(), json);
            report = merged.ToJsonString();
        }
        bool singleWindow = System.Windows.Application.Current.Windows.Count == 1;
        if (!singleWindow) throw new InvalidOperationException("A verificação encontrou mais de uma janela principal.");
        var fullReport = JsonNode.Parse(report)!.AsObject();
        fullReport["native"] = JsonSerializer.SerializeToNode(new { singleWindow });
        File.WriteAllText(Path.ChangeExtension(path, ".json"), fullReport.ToJsonString());
        await Task.Delay(1600);
        await CaptureAsync(path);
        closing = true; Close();
    }
}
