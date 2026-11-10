using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace Mirage;

public sealed partial class WebWindow
{
    private bool recoveryVisible;
    private Task RetryUiAsync() { recoveryVisible = false; return EnsureUiAsync(); }

    private void ShowUiFailure(WebView2? failed, string message)
    {
        if (closing || (failed is not null && !ReferenceEquals(failed, view))) return;
        backgroundRelease?.Cancel();
        uiReady = false; recoveryVisible = true;
        view = null; uiInitialization = null; lastPostedState = null;
        if (!engine.HasWebSession) updates.Stop();
        try { failed?.Dispose(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        var panel = new StackPanel { Width = 420, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "Vamos reabrir sua interface", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = message + "\n\nAs sequências e a sessão são mantidas pelo Mirage.", TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 24) });
        var retry = new Button { Content = "Reabrir interface", Padding = new Thickness(18, 10, 18, 10), Background = new SolidColorBrush(Color.FromRgb(141, 134, 232)), Foreground = Brushes.White };
        retry.Click += async (_, _) => { retry.IsEnabled = false; await RetryUiAsync(); };
        panel.Children.Add(retry);
        var close = new Button { Content = engine.HasWebSession ? "Continuar na bandeja" : "Fechar Mirage", Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
        retry.Focus();
    }
}
