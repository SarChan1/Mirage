using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Mirage;

public sealed partial class WebWindow
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);
    private const int BackgroundReleaseSeconds = 15;
    private static readonly TimeSpan ForegroundTickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackgroundTickInterval = TimeSpan.FromSeconds(5);
    private CancellationTokenSource? backgroundRelease;
    private string? uiSnapshot;
    private int visibilityRevision;
    private double lastMemoryReleaseMilliseconds;
    private bool UiVisible => IsVisible && WindowState != WindowState.Minimized;

    private void UpdateVisibility()
    {
        if (closing) return;
        int revision = ++visibilityRevision;
        backgroundRelease?.Cancel(); backgroundRelease?.Dispose(); backgroundRelease = null;
        if (UiVisible) {
            updates.Interval = ForegroundTickInterval;
            try { view?.CoreWebView2?.Resume(); } catch (InvalidOperationException) { }
            if (!recoveryVisible || engine.HasWebSession) updates.Start(); else updates.Stop();
            if (!recoveryVisible) _ = EnsureUiAsync();
        } else {
            updates.Interval = BackgroundTickInterval;
            if (!engine.HasWebSession) updates.Stop();
            var cancellation = new CancellationTokenSource(); backgroundRelease = cancellation;
            _ = SuspendUiAsync(view, revision);
            _ = ReleaseUiLaterAsync(revision, cancellation.Token);
        }
    }

    private async Task SuspendUiAsync(WebView2? current, int revision)
    {
        if (!uiReady || current?.CoreWebView2 is null) return;
        try {
            string snapshot = await current.ExecuteScriptAsync("window.dispatchEvent(new Event('mirage.before-background')); window.__MIRAGE_CAPTURE_VIEW__?.() ?? null").WaitAsync(TimeSpan.FromSeconds(2));
            if (snapshot.Length < 256_000) {
                using var document = JsonDocument.Parse(snapshot);
                if (document.RootElement.ValueKind == JsonValueKind.Object) uiSnapshot = snapshot;
            }
            if (closing || UiVisible || revision != visibilityRevision || !ReferenceEquals(view, current)) return;
            await current.CoreWebView2.TrySuspendAsync();
            // A restore can race an asynchronous suspension; the visible page must always resume.
            if (!closing && ReferenceEquals(view, current) && (UiVisible || revision != visibilityRevision)) current.CoreWebView2.Resume();
        } catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or JsonException) { }
    }

    private async Task ReleaseUiLaterAsync(int revision, CancellationToken cancellation)
    {
        try {
            await Task.Delay(TimeSpan.FromSeconds(BackgroundReleaseSeconds), cancellation);
            if (uiInitialization is { IsCompleted: false } pending) await pending.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            if (closing || UiVisible || revision != visibilityRevision || cancellation.IsCancellationRequested) return;
            var old = view;
            view = null; uiReady = false; uiInitialization = null; lastPostedState = null;
            if (!recoveryVisible) Content = null;
            old?.Dispose();
            old = null;
            engine.ReleaseLibraryForBackground();
            // After dropping the browser and catalog, return unused heap pages once per hide.
            // Yield first so completed native callbacks can let go of their last references.
            await Task.Yield();
            if (!closing && !UiVisible && revision == visibilityRevision) {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                using var process = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(process.Handle, -1, -1);
                lastMemoryReleaseMilliseconds = watch.Elapsed.TotalMilliseconds;
            }
        } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException) { }
    }
}
