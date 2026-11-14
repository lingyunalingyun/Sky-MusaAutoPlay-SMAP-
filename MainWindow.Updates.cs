using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

public partial class MainWindow
{
    DeltaUpdateService? _deltaUpdates;
    ChromeWindow? _updateReadyNotice;
    bool _updateReadyOffered;
    bool _deltaRestartPreparing;
    internal bool BackgroundUpdatesEnabled => _deltaUpdates?.Supported == true;
    void ConfirmDeltaUpdateLaunch()
    {
        var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--smap-update-health");
        if (at < 0 || at + 1 >= args.Length) return;
        try { Updates.UpdateHealth.Confirm(AppContext.BaseDirectory, UpdateChecker.AppVersion, args[at + 1], Updates.UpdateTrust.Keys); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or FormatException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
        { Logger.Info("Update launch confirmation failed: " + error.GetType().Name); }
    }
    void InitializeDeltaUpdates()
    {
        _deltaUpdates = new(); UpdatePane.Visibility = _deltaUpdates.Supported ? Visibility.Visible : Visibility.Collapsed;
        SetUpdateRow.Visibility = _deltaUpdates.Supported ? Visibility.Collapsed : Visibility.Visible;
        UpdatePane.ActionRequested += async () =>
        {
            if (_deltaUpdates.State is DeltaUpdateState.Downloading or DeltaUpdateState.Reconstructing) _deltaUpdates.Pause();
            else await _deltaUpdates.Start(manual: true);
        };
        UpdatePane.RestartRequested += async () => await RestartForDeltaUpdate();
        _deltaUpdates.Changed += () => UpdatePane.Refresh(_deltaUpdates);
        _deltaUpdates.Found += () => ShowToast(Lang.Update("found"));
        _deltaUpdates.Ready += ShowUpdateReady;
        Activated += (_, _) => { if (_deltaUpdates.State == DeltaUpdateState.Ready) ShowUpdateReady(); };
    }
    void ShowUpdateReady()
    {
        if (_updateReadyOffered || !IsVisible || WindowState == WindowState.Minimized || _deltaUpdates?.State != DeltaUpdateState.Ready) return;
        _updateReadyOffered = true;
        var window = new ChromeWindow(Lang.Update("ready"), 440) { Owner = this, ShowActivated = false };
        _updateReadyNotice = window;
        var body = new StackPanel { Margin = new Thickness(22) };
        body.Children.Add(new TextBlock { Text = Lang.Update("readyBody"), FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextFg"), Margin = new Thickness(0, 0, 0, 18) });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; body.Children.Add(actions);
        foreach (bool primary in new[] { true, false })
        {
            var button = new Button { Content = Lang.Update(primary ? "restart" : "later"), MinWidth = primary ? 160 : 110, Height = 38, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(6, 0, 0, 0), Template = ChromeWindow.BtnTpl("0") };
            button.SetResourceReference(Control.BackgroundProperty, primary ? "PracticeActionBg" : "NeutralBtnBg"); button.SetResourceReference(Control.ForegroundProperty, primary ? "ActionFg" : "NeutralBtnFg");
            button.Click += async (_, _) => { window.Close(); if (primary) await RestartForDeltaUpdate(); }; actions.Children.Add(button);
        }
        window.SetBody(body); window.Closed += (_, _) => _updateReadyNotice = null; window.Show();
    }
    async Task RestartForDeltaUpdate()
    {
        if (_deltaUpdates?.State != DeltaUpdateState.Ready) return;
        if (_deltaRestartPreparing) return;
        _deltaRestartPreparing = true;
        Process? worker = null;
        try
        {
            _updateReadyNotice?.Close();
            // Ask before starting the timed handoff. A user may need more than two minutes to review or save an editor.
            foreach (var editor in _editors.ToArray())
            {
                if (!IsEnabled || !editor.IsEnabled || System.Windows.Interop.ComponentDispatcher.IsThreadModal) return;
                editor.Close(); if (_editors.Contains(editor)) return;
            }
            worker = await _deltaUpdates.PrepareRestart();
            Close(); // Existing editor closing handlers offer Save / Discard / Cancel.
            if (IsVisible && !worker.HasExited) worker.Kill(); // Cancel only the helper we just created; application remains open.
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (worker != null && !worker.HasExited) worker.Kill(); ShowToast(Lang.Update("handoffError")); }
        finally { worker?.Dispose(); _deltaRestartPreparing = false; }
    }
}
