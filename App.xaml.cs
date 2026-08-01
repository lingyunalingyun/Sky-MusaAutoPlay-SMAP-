using System;
using System.Windows;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class App : Application
{
    public App()
    {
        Logger.Info($"===== SMAP v{UpdateChecker.AppVersion} 启动 =====");
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception("未知错误");
            Logger.Exception("非UI线程未处理异常", ex);
            HandleCrash("程序崩溃:\n" + ex.Message, fatal: true);
        };
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Exception("UI线程未处理异常", e.Exception);
        e.Handled = true;   // 不让它直接崩掉
        HandleCrash("程序出现错误:\n" + e.Exception.Message, fatal: false);
    }

    // 崩溃时: 记完日志 → 问是否上传 → 上传
    void HandleCrash(string msg, bool fatal)
    {
        if (!Ask(msg + "\n\n是否上传日志帮助开发者修复?")) return;
        if (fatal)
        {
            // 进程即将退出, 同步等一下让上传有机会完成
            var err = CloudApi.UploadLogAsync(Logger.Recent(3)).GetAwaiter().GetResult();
            Notify(err);
        }
        else _ = UploadThenNotify();
    }

    async System.Threading.Tasks.Task UploadThenNotify() => Notify(await CloudApi.UploadLogAsync(Logger.Recent(3)));

    static bool Ask(string msg)
    {
        try { return MsgBox.Confirm(Current?.MainWindow, msg, "SMAP 出错"); }
        catch { return MessageBox.Show(msg, "SMAP 出错", MessageBoxButton.YesNo) == MessageBoxResult.Yes; }
    }

    static void Notify(string? err)
    {
        var m = err == null ? "日志已上传，感谢反馈！" : "上传失败: " + err;
        try { MsgBox.Info(Current?.MainWindow, m); } catch { MessageBox.Show(m); }
    }
}
