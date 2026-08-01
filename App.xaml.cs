using System.Windows;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class App : Application
{
    public App()
    {
        // 全局捕获未处理异常, 弹窗显示原因而非静默崩溃 (便于定位)
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show((e.ExceptionObject as Exception)?.ToString() ?? "未知错误", "SMAP 崩溃 (非 UI 线程)");
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "SMAP 错误");
        e.Handled = true; // 不让它直接崩掉
    }
}
