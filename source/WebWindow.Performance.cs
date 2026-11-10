using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Mirage;

public sealed partial class WebWindow
{
    private sealed record MemorySample(long PrivateBytes, long WorkingSetBytes, int Processes, long ManagedBytes, long ManagedCommittedBytes);
    private static MemorySample Sample(int[] ids)
    {
        long bytes = 0, resident = 0; int count = 0;
        foreach (int id in ids.Distinct()) {
            try { using var process = Process.GetProcessById(id); process.Refresh(); if (process.HasExited) continue; bytes += process.PrivateMemorySize64; resident += process.WorkingSet64; count++; }
            catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        return new(bytes, resident, count, GC.GetTotalMemory(false), GC.GetGCMemoryInfo().TotalCommittedBytes);
    }
    private async Task WaitForUiAsync()
    {
        await EnsureUiAsync();
        for (int i = 0; i < 200; i++) {
            if (uiReady && view?.CoreWebView2 is not null && await view.ExecuteScriptAsync("Boolean(window.__MIRAGE_READY__)") == "true") return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("A interface não retornou após o repouso.");
    }
    private async Task<object> RunRecoveryChecksAsync()
    {
        if (!PreviewOnly) throw new InvalidOperationException("Conferência exige modo de prévia.");
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        using var empty = JsonDocument.Parse("{}");
        await engine.DispatchWebAsync("previewSession", empty.RootElement);
        string before = JsonSerializer.SerializeToElement(engine.GetWebState(), json).GetProperty("session").GetProperty("game").GetProperty("id").GetString()!;
        var renderer = browser.CoreWebView2.Environment.GetProcessInfos().First(info => info.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ProcessKind.Renderer);
        using (var ownedRenderer = Process.GetProcessById(renderer.ProcessId)) ownedRenderer.Kill();
        for (int i = 0; i < 200 && !recoveryVisible; i++) await Task.Delay(100);
        Check(recoveryVisible && view is null, "an actual preview renderer crash displays the native recovery screen");
        Check(engine.HasWebSession, "renderer failure never ends the controller session");
        await RetryUiAsync(); await WaitForUiAsync();
        Check(uiReady && !recoveryVisible && view?.CoreWebView2 is not null, "retry recreates the interface after a renderer crash");
        var state = JsonSerializer.SerializeToElement(engine.GetWebState(), json);
        Check(state.GetProperty("session").GetProperty("game").GetProperty("id").GetString() == before && state.GetProperty("playlist").GetArrayLength() == 4, "recovery preserves the active game and sequence");
        Check(await browser.ExecuteScriptAsync("Boolean(document.querySelector('[data-testid=runtime-panel]'))") == "true", "recreated React interface receives the still-running native session");
        int browserId = checked((int)browser.CoreWebView2.BrowserProcessId);
        using (var ownedBrowser = Process.GetProcessById(browserId)) ownedBrowser.Kill();
        for (int i = 0; i < 200 && !recoveryVisible; i++) await Task.Delay(100);
        Check(recoveryVisible && view is null && engine.HasWebSession, "an actual preview browser-process crash offers recovery without stopping the session");
        await RetryUiAsync(); await WaitForUiAsync();
        Check(uiReady && !recoveryVisible && browser.CoreWebView2.BrowserProcessId != browserId, "retry recreates the browser environment after its process exits");
        // Hold periodic preview posts while checking a synthetic storage-error UI fixture.
        updates.Stop();
        var warningState = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(engine.GetWebState(), json))!.AsObject();
        warningState["storage"] = JsonSerializer.SerializeToNode(new { warning = "Não foi possível ler suas preferências. O arquivo original foi preservado.", replacementRequired = true }, json);
        Post(new { type = "state", state = warningState }); await Task.Delay(200);
        Check(await browser.ExecuteScriptAsync("Boolean(document.querySelector('[data-testid=storage-warning]'))") == "true", "storage failure is rendered as a persistent warning rather than a success toast");
        await browser.ExecuteScriptAsync("document.querySelector('[data-testid=storage-warning] button').click()"); await Task.Delay(250);
        Check(await browser.ExecuteScriptAsync("Boolean(document.querySelector('[role=dialog]')?.textContent.includes('serão perdidos'))") == "true", "replacing unreadable preferences requires an explicit explanation and confirmation");
        await browser.ExecuteScriptAsync("Array.from(document.querySelectorAll('[role=dialog] button')).find(b => b.textContent.includes('Manter arquivo original')).click()");
        for (int i = 0; i < 60 && await browser.ExecuteScriptAsync("Boolean(document.querySelector('[role=dialog]'))") == "true"; i++) await Task.Delay(50);
        Check(await browser.ExecuteScriptAsync("!document.querySelector('[role=dialog]') && Boolean(document.querySelector('[data-testid=storage-warning]'))") == "true", "canceling recovery closes the dialog and keeps the storage warning visible");
        await engine.DispatchWebAsync("stop", empty.RootElement);
        Post(new { type = "state", state = engine.GetWebState() });
        updates.Start();
        return new { checks, testOnly = true };
    }

    private async Task<object> RunBackgroundChecksAsync(bool extended = false)
    {
        if (!PreviewOnly) throw new InvalidOperationException("Conferência exige modo de prévia.");
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        using var empty = JsonDocument.Parse("{}");
        var session = await engine.DispatchWebAsync("previewSession", empty.RootElement);
        Post(new { type = "state", state = session });
        await Task.Delay(1500);
        string beforeSnapshot = await browser.ExecuteScriptAsync("window.__MIRAGE_CAPTURE_VIEW__()");
        var beforeState = JsonSerializer.SerializeToElement(engine.GetWebState(), json);
        int[] ids = browser.CoreWebView2.Environment.GetProcessInfos().Select(info => info.ProcessId).Append(Environment.ProcessId).ToArray();
        var foreground = Sample(ids);
        int beforeTicks = backendTicks;
        Hide();
        await Task.Delay(TimeSpan.FromSeconds(BackgroundReleaseSeconds + 4));
        Check(view is null, "hidden window disposes its WebView2 after the idle delay");
        Check(engine.IsLibrarySuspended && engine.LoadedGameCount <= beforeState.GetProperty("playlist").GetArrayLength() + 2, "background controller retains only games needed for the queue and current selection");
        Check(engine.HasWebSession, "session remains active without a browser");
        Check(updates.IsEnabled && updates.Interval == BackgroundTickInterval && backendTicks > beforeTicks + 2, "low-frequency backend timer continues while a session is active in the tray");
        var afterState = JsonSerializer.SerializeToElement(engine.GetWebState(), json);
        Check(afterState.GetProperty("session").GetProperty("game").GetProperty("id").GetString() == beforeState.GetProperty("session").GetProperty("game").GetProperty("id").GetString(), "headless mode preserves the current game");
        Check(afterState.GetProperty("session").GetProperty("remainingSeconds").GetDouble() < beforeState.GetProperty("session").GetProperty("remainingSeconds").GetDouble() - 15, "session countdown keeps advancing while the page is absent");
        var background = Sample(ids);
        var headlessSamples = new List<MemorySample> { background };
        using var process = Process.GetCurrentProcess();
        var wall = Stopwatch.StartNew(); var cpu = process.TotalProcessorTime;
        await Task.Delay(5000);
        process.Refresh();
        double cpuPercent = (process.TotalProcessorTime - cpu).TotalSeconds / wall.Elapsed.TotalSeconds * 100;
        Check(background.Processes < foreground.Processes, "WebView2 process resources are released instead of merely hidden");
        if (extended) {
            for (int i = 0; i < 3; i++) { await Task.Delay(TimeSpan.FromSeconds(20)); headlessSamples.Add(Sample(ids)); }
            Check(headlessSamples.All(sample => sample.Processes == 1), "extended headless run never recreates browser processes");
            Check(headlessSamples[^1].PrivateBytes < background.PrivateBytes + 64 * 1024 * 1024, "private committed memory stays within a 64 MiB growth allowance during the extended sample");
            Check(engine.HasWebSession && view is null, "extended headless run keeps the session without recreating the interface");
        }
        Show(); WindowState = WindowState.Normal;
        await WaitForUiAsync(); await Task.Delay(700);
        Check(!engine.IsLibrarySuspended && engine.LoadedGameCount > 20000, "restoring the window reloads the searchable catalog");
        string restored = await browser.ExecuteScriptAsync("window.__MIRAGE_CAPTURE_VIEW__()");
        Check(restored == beforeSnapshot, "view, query, filters, selected games and duration survive browser recreation");
        var restoredState = JsonSerializer.SerializeToElement(engine.GetWebState(), json);
        Check(restoredState.GetProperty("playlist").GetArrayLength() == beforeState.GetProperty("playlist").GetArrayLength(), "playlist survives browser recreation");
        for (int i = 0; i < 3; i++) { Hide(); await Task.Delay(100); Show(); WindowState = WindowState.Normal; await Task.Delay(150); }
        await WaitForUiAsync();
        Check(view?.CoreWebView2?.IsSuspended == false, "rapid hide and restore never leaves the visible page suspended");
        await engine.DispatchWebAsync("stop", empty.RootElement);
        Hide(); await Task.Delay(150);
        Check(!updates.IsEnabled, "idle background app stops its session timer");
        Show(); WindowState = WindowState.Normal; await WaitForUiAsync();
        Post(new { type = "state", state = engine.GetWebState() });
        return new { checks, foreground, background, headlessSamples, extendedSampleSeconds = extended ? 60 : 0, backgroundCpuPercentOfOneCore = cpuPercent, memoryReleaseMilliseconds = lastMemoryReleaseMilliseconds, idleDelaySeconds = BackgroundReleaseSeconds, testOnly = true };
    }
}
