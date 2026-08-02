using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>深/浅主题: 把可切换颜色写进 Application.Resources(供 DynamicResource 绑定), 运行时切换 + 持久化。
/// 彩色强调按钮两主题不变, 只切中性色(底/面板/文字/输入框/中性按钮/琴键)。</summary>
public static class Theme
{
    public static bool Dark { get; private set; } = true;

    static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "theme.txt");

    public static bool LoadDark()
    {
        try { if (System.IO.File.Exists(File)) return System.IO.File.ReadAllText(File).Trim() != "light"; }
        catch { }
        return true;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, Dark ? "dark" : "light");
        }
        catch { }
    }

    // 琴键视觉色(代码里用, 随主题变)
    public static Color KeySquare, KeyLit, KeyLetter, KeyDiamond, KeyBorder;
    public static readonly Color KeyWait = Color.FromRgb(0xE6, 0x7E, 0x22);
    // 曲库列表悬停/选中色(代码建 ItemContainerStyle 用)
    public static Color ListHover, ListSel;

    public static void Apply(bool dark)
    {
        Dark = dark;
        var r = Application.Current.Resources;

        void C(string key, string darkHex, string lightHex) => r[key] = Br(dark ? darkHex : lightHex);

        C("WindowBg",     "#121214", "#ffffff");
        C("WindowBorder", "#33335a", "#d2d2da");
        C("PanelBg",      "#1c1c1c", "#eef0f2");
        C("TextFg",       "#e0e0e0", "#222222");
        C("SubTextFg",    "#9a9ab0", "#6a6a72");
        C("StatusFg",     "#9fd39f", "#2f8a3f");
        C("BoxBg",        "#262626", "#eef0f2");
        C("BoxBorder",    "#4a4a4a", "#d2d2da");
        C("NeutralBtnBg", "#3a3a3a", "#eef0f2");
        C("NeutralBtnFg", "#f0f0f0", "#333333");
        C("BtnBorder",    "#585858", "#c6c6d0");   // 按钮描边(深浅都有), 让按钮从背景里分出来
        C("ListBg",       "#181818", "#ffffff");
        C("ListBorder",   "#333333", "#d8d8e0");
        C("ComboBg",      "#2a2a2a", "#eef0f2");
        C("ScrollThumbBrush", "#4a4a4a", "#c2c2ca");
        C("CaptionFg",    "#cfcfe0", "#ffffff");
        C("MenuHi",       "#3a3a46", "#dfe6f2");   // 右键菜单 / 行 hover 高亮
        C("RowSel",       "#50505f", "#c4d4ec");   // 列表选中(深灰, 非蓝)
        C("Accent",       "#5aa0ff", "#2f6fd0");   // 主题强调色(进度条等)
        C("ProgTrackBg",  "#40ffffff", "#33000000");   // 进度条未播放轨道(深浅各自可见)

        r["TitleGrad"] = dark
            ? Grad("#1c1c3e", "#2a2352", "#241d47")
            : Grad("#4a63ff", "#6a4bff", "#8a52ff");

        // 琴键色
        KeySquare  = Col(dark ? "#4a4a4a" : "#e2e2e6");
        KeyLit     = Col(dark ? "#222222" : "#c6c6cc");
        KeyLetter  = Col(dark ? "#f2f2f2" : "#333333");
        KeyDiamond = Col(dark ? "#c4c4d6" : "#9a9aa2");
        KeyBorder  = Col(dark ? "#585858" : "#c6c6d0");
        ListHover  = Col(dark ? "#2f2f2f" : "#e6eaf2");
        ListSel    = Col(dark ? "#2d5a88" : "#cfe0f5");

        Save();
    }

    static SolidColorBrush Br(string hex) { var b = new SolidColorBrush(Col(hex)); b.Freeze(); return b; }
    static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static LinearGradientBrush Grad(string a, string b, string c)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Col(a), 0));
        g.GradientStops.Add(new GradientStop(Col(b), 0.6));
        g.GradientStops.Add(new GradientStop(Col(c), 1));
        g.Freeze();
        return g;
    }
}
