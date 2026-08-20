using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SMAP_WPF;

public sealed class UpdateProgressWindow : ChromeWindow
{
    readonly ProgressBar _progress = new() { Height = 18, Minimum = 0, Maximum = 100 };
    readonly TextBlock _status = new() { Text = "正在从 GitHub 下载更新… 0%", Margin = new Thickness(0, 0, 0, 12) };
    readonly CancellationTokenSource _cancel = new();
    readonly UpdateChecker.Release _release;
    bool _installStarted;

    UpdateProgressWindow(Window owner, UpdateChecker.Release release) : base("更新 SMAP", 440)
    {
        Owner = owner;
        _release = release;
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        body.Children.Add(_status);
        body.Children.Add(_progress);
        SetBody(body);
        Loaded += StartDownload;
        Closed += (_, __) => { if (!_installStarted) _cancel.Cancel(); };
    }

    public static bool Run(Window owner, UpdateChecker.Release release)
        => new UpdateProgressWindow(owner, release).ShowDialog() == true;

    async void StartDownload(object sender, RoutedEventArgs e)
    {
        try
        {
            var progress = new Progress<double>(value =>
            {
                _progress.Value = value;
                _status.Text = $"正在从 GitHub 下载更新… {value:0}%";
            });
            var setup = await UpdateChecker.DownloadAsync(_release, progress, _cancel.Token);
            _status.Text = "下载完成，正在启动安装…";

            var psi = new ProcessStartInfo(setup) { UseShellExecute = true };
            psi.ArgumentList.Add("--update");
            psi.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd('\\'));
            psi.ArgumentList.Add("--restart");
            psi.ArgumentList.Add(Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "SMAP.exe"));
            psi.ArgumentList.Add("--wait-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(psi);
            _installStarted = true;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            if (IsVisible) DialogResult = false;
        }
        catch (Exception ex)
        {
            MsgBox.Info(this, "自动更新失败：\n" + ex.Message, "更新 SMAP");
            DialogResult = false;
        }
    }
}
