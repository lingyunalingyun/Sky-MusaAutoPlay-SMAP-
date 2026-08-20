using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SMAP_Installer;

public partial class MainWindow : Window
{
    readonly Grid[] _steps;
    int _step;
    string _installedExe = "";
    string? _updateTarget, _restartExe;
    int _waitPid;

    public MainWindow()
    {
        InitializeComponent();
        _steps = new[] { Step1, Step2, Step3, Step4, Step5 };
        LicenseText.Text = License;
        ReadUpdateArguments();
        if (_updateTarget != null) Loaded += AutoUpdate_Loaded;
    }

    void ReadUpdateArguments()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i + 1 < args.Length; i++)
        {
            if (args[i] == "--update") _updateTarget = args[++i];
            else if (args[i] == "--restart") _restartExe = args[++i];
            else if (args[i] == "--wait-pid" && int.TryParse(args[++i], out int pid)) _waitPid = pid;
        }
        if (_updateTarget == null) return;
        _updateTarget = Path.GetFullPath(_updateTarget);
        _restartExe = _restartExe == null ? null : Path.GetFullPath(_restartExe);
        if (_restartExe == null || !string.Equals(Path.GetDirectoryName(_restartExe), _updateTarget, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(_restartExe), "SMAP.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetPathRoot(_updateTarget), _updateTarget, StringComparison.OrdinalIgnoreCase))
            _updateTarget = null;
    }

    async void AutoUpdate_Loaded(object sender, RoutedEventArgs e)
    {
        ShowStep(3);
        SetProgress(0);
        Log("正在等待 SMAP 退出……");
        try
        {
            await Task.Run(() =>
            {
                if (_waitPid > 0)
                {
                    try { System.Diagnostics.Process.GetProcessById(_waitPid).WaitForExit(); }
                    catch (ArgumentException) { }
                }
                Extract(_updateTarget!);
            });
            _installedExe = _restartExe!;
            try { CreateShortcuts(_updateTarget!); Log("已更新快捷方式。"); }
            catch (Exception se) { Log("⚠ 快捷方式更新失败: " + se.Message); }
            Log("更新完成，正在重新启动 SMAP……");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_installedExe) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            Log("❌ 更新失败: " + ex.Message);
            MessageBox.Show(this, "自动更新失败:\n" + ex.Message, "SMAP 更新");
        }
    }

    // ---- 窗口控制 ----
    void Title_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Min_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void CloseDot_Click(object sender, MouseButtonEventArgs e) => Close();

    // ---- 步骤切换 ----
    void ShowStep(int i)
    {
        _step = i;
        for (int k = 0; k < _steps.Length; k++)
            _steps[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
    }
    void Next_Click(object sender, RoutedEventArgs e) => ShowStep(Math.Min(_step + 1, _steps.Length - 1));
    void Prev_Click(object sender, RoutedEventArgs e) => ShowStep(Math.Max(_step - 1, 0));

    // ---- 选路径 ----
    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择安装目录" };
        if (dlg.ShowDialog(this) == true)
            PathBox.Text = Path.Combine(dlg.FolderName, "SMAP");
    }

    // ---- 安装(解压嵌入的 app.zip 到目标路径) ----
    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var target = PathBox.Text.Trim();
        if (target.Length == 0) { MessageBox.Show(this, "请填写安装路径"); return; }
        ShowStep(3);   // 进度页
        SetProgress(0);
        try
        {
            await Task.Run(() => Extract(target));
            _installedExe = Path.Combine(target, "SMAP.exe");
            try { CreateShortcuts(target); Log("已创建桌面 + 开始菜单快捷方式。"); }
            catch (Exception se) { Log("⚠ 快捷方式创建失败: " + se.Message); }
            Log("完成。");
            ShowStep(4);   // 完成页
        }
        catch (Exception ex)
        {
            Log("❌ 安装失败: " + ex.Message);
            MessageBox.Show(this, "安装失败:\n" + ex.Message, "SMAP 安装");
        }
    }

    void Extract(string target)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("app.zip")
            ?? throw new Exception("安装包内未找到 SMAP 数据 (payload 未打入)。请用 build-installer 脚本重新打包。");
        using var zip = new ZipArchive(s, ZipArchiveMode.Read);
        Directory.CreateDirectory(target);

        int total = zip.Entries.Count, done = 0;
        foreach (var entry in zip.Entries)
        {
            // Compress-Archive(PS5.1) 用反斜杠, 统一成 /
            var rel = entry.FullName.Replace('\\', '/');
            var dest = Path.GetFullPath(Path.Combine(target, rel));
            // 目录条目: entry.Name 为空(不依赖分隔符判断, 最稳)
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            done++;
            int pct = (int)(done * 100.0 / Math.Max(1, total));
            Dispatcher.Invoke(() => { SetProgress(pct); Log("解压: " + rel); });
        }
        Dispatcher.Invoke(() => SetProgress(100));
    }

    // 桌面 + 开始菜单快捷方式(全体用户, 因安装器是管理员); 开始菜单项让 Windows 搜索/PowerToys Run 找得到
    void CreateShortcuts(string installDir)
    {
        var exe = Path.Combine(installDir, "SMAP.exe");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var startPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        Directory.CreateDirectory(startPrograms);
        MakeShortcut(Path.Combine(desktop, "SMAP.lnk"), exe, installDir);
        MakeShortcut(Path.Combine(startPrograms, "SMAP.lnk"), exe, installDir);
    }

    static void MakeShortcut(string lnkPath, string target, string workDir)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new Exception("WScript.Shell 不可用");
        dynamic shell = Activator.CreateInstance(t)!;
        var sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = target;
        sc.WorkingDirectory = workDir;
        sc.Description = "光遇-Musa 自动演奏 (SMAP)";
        sc.IconLocation = target + ",0";
        sc.Save();
    }

    void SetProgress(int pct)
    {
        ProgText.Text = pct + "%";
        ProgFill.Width = pct / 100.0 * ProgBar.ActualWidth;
    }

    void Log(string line)
    {
        LogText.Text += line + "\n";
        LogScroll.ScrollToEnd();
    }

    // ---- 完成: 立刻打开 ----
    void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_installedExe))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_installedExe) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "打开失败: " + ex.Message); }
        }
        Close();
    }

    const string License =
        "免责声明\n\n" +
        "1. SMAP（Sky-MusaAutoPlay）是一款《光·遇》自动弹琴辅助工具，仅供学习交流与个人娱乐使用。\n" +
        "2. 使用本软件产生的一切后果（包括但不限于账号处罚）由使用者自行承担，作者不承担任何责任。\n" +
        "3. 请遵守《光·遇》官方用户协议，切勿用于商业用途或影响他人游戏体验。\n\n" +
        "开源说明\n\n" +
        "本软件为开源项目，源代码托管于 GitHub：\n" +
        "https://github.com/lingyunalingyun/Sky-MusaAutoPlay-SMAP-\n\n" +
        "本软件免费，若你是付费获得的，说明你被骗了。\n" +
        "点击「我同意所有协议，下一步」即表示你已阅读并接受以上条款。";
}
