using System;
using System.IO;

namespace SMAP_WPF;

/// <summary>简单文件日志: 写 %APPDATA%\SMAP\logs\smap-YYYYMMDD.log。捕获崩溃/错误/关键事件, 供"上传日志"取用。</summary>
public static class Logger
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "logs");
    static readonly object _lock = new();

    public static string TodayFile => Path.Combine(Dir, $"smap-{DateTime.Now:yyyyMMdd}.log");
    public static string LogDir => Dir;

    public static void Log(string level, string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            lock (_lock)
                File.AppendAllText(TodayFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    public static void Info(string m) => Log("INFO", m);
    public static void Warn(string m) => Log("WARN", m);
    public static void Error(string m) => Log("ERROR", m);
    public static void Exception(string where, Exception e) => Log("ERROR", $"{where}: {e}");

    /// <summary>返回最近 N 天日志合并文本(供上传/复制)。</summary>
    public static string Recent(int days = 3)
    {
        try
        {
            if (!Directory.Exists(Dir)) return "(暂无日志)";
            var files = Directory.GetFiles(Dir, "smap-*.log");
            Array.Sort(files);
            var pick = files.Length > days ? files[^days..] : files;
            var sb = new System.Text.StringBuilder();
            foreach (var f in pick)
            {
                sb.AppendLine($"===== {Path.GetFileName(f)} =====");
                sb.AppendLine(File.ReadAllText(f));
            }
            return sb.Length == 0 ? "(暂无日志)" : sb.ToString();
        }
        catch (Exception e) { return "读取日志失败: " + e.Message; }
    }
}
