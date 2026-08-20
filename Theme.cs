using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace SMAP_WPF;

public enum AppTheme { Dark, Light, SunsetPink }

/// <summary>深/浅主题: 把可切换颜色写进 Application.Resources(供 DynamicResource 绑定), 运行时切换 + 持久化。
/// 彩色强调按钮两主题不变, 只切中性色(底/面板/文字/输入框/中性按钮/琴键)。</summary>
public static class Theme
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;
    public static bool Dark => Current == AppTheme.Dark;
    public static string LangKey => Current switch { AppTheme.Light => "theme.light", AppTheme.SunsetPink => "theme.sunset", _ => "theme.dark" };

    static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "theme.txt");

    public static AppTheme Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return System.IO.File.ReadAllText(File).Trim() switch
                {
                    "light" => AppTheme.Light,
                    "sunset" => AppTheme.SunsetPink,
                    _ => AppTheme.Dark
                };
        }
        catch { }
        return AppTheme.Dark;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, Current switch { AppTheme.Light => "light", AppTheme.SunsetPink => "sunset", _ => "dark" });
        }
        catch { }
    }

    // 琴键视觉色(代码里用, 随主题变)
    public static Color KeySquare, KeyLit, KeyLetter, KeyDiamond, KeyBorder;
    public static readonly Color KeyWait = Color.FromRgb(0xE6, 0x7E, 0x22);
    // 曲库列表悬停/选中色(代码建 ItemContainerStyle 用)
    public static Color ListHover, ListSel;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        bool dark = theme == AppTheme.Dark;
        bool sunset = theme == AppTheme.SunsetPink;
        var r = Application.Current.Resources;

        string Pick(string darkHex, string lightHex, string sunsetHex) => dark ? darkHex : sunset ? sunsetHex : lightHex;
        void C(string key, string darkHex, string lightHex, string sunsetHex) => r[key] = Br(Pick(darkHex, lightHex, sunsetHex));

        C("WindowBg",     "#121214", "#ffffff", "#FFF3FC");
        C("WindowBorder", "#33335a", "#d2d2da", "#D9B8F2");
        C("PanelBg",      "#1c1c1c", "#eef0f2", "#FFF8FD");
        C("TextFg",       "#e0e0e0", "#222222", "#2E2530");
        C("SubTextFg",    "#9a9ab0", "#6a6a72", "#756977");
        C("StatusFg",     "#9fd39f", "#2f8a3f", "#B64F91");
        C("BoxBg",        "#262626", "#eef0f2", "#FFFFFF");
        C("BoxBorder",    "#4a4a4a", "#d2d2da", "#E4C7F3");
        C("NeutralBtnBg", "#3a3a3a", "#eef0f2", "#FFEAF8");
        C("NeutralBtnFg", "#f0f0f0", "#333333", "#352B37");
        C("BtnBorder",    "#585858", "#c6c6d0", "#DDB9EE");
        C("ListBg",       "#181818", "#ffffff", "#FFFFFF");
        C("ListBorder",   "#333333", "#d8d8e0", "#E4C7F3");
        C("ComboBg",      "#2a2a2a", "#eef0f2", "#FFF9FD");
        C("ScrollThumbBrush", "#4a4a4a", "#c2c2ca", "#D6B6D8");
        C("CaptionFg",    "#cfcfe0", "#ffffff", "#2E2530");
        C("MenuHi",       "#3a3a46", "#dfe6f2", "#F8DDF3");
        C("RowSel",       "#50505f", "#c4d4ec", "#EDC9F4");
        C("Accent",       "#5aa0ff", "#2f6fd0", "#F45FB7");
        C("ProgTrackBg",  "#40ffffff", "#33000000", "#35A66A91");
        C("ActionFg",     "#ffffff", "#ffffff", "#2E2530");
        C("CreateActionFg", "#ffffff", "#ffffff", "#2E2530");
        C("FavoriteBrush", "#E6B52A", "#F1C84B", "#FFD85C");

        r["TitleGrad"] = dark ? Grad("#1c1c3e", "#2a2352", "#241d47")
            : sunset ? Grad("#DD77F1", "#F4C0F4", "#F56AC8")
            : Grad("#4a63ff", "#6a4bff", "#8a52ff");
        r["LocalActiveBg"] = sunset ? Grad("#BDEEFF", "#D8E8FF", "#FFD5DF") : Br(dark ? "#2F6FD0" : "#457FD6");
        r["CloudActiveBg"] = sunset ? Grad("#BDF7DD", "#D9F6E5", "#FFD5DF") : Br(dark ? "#12795A" : "#278B69");
        r["NavInactiveBg"] = Br(sunset ? "#F6EAF5" : dark ? "#2B2B2B" : "#E5E7EB");
        r["SideActionBg"] = sunset ? Grad("#EBA8FF", "#F8B8ED", "#FFD9C7") : Br(dark ? "#3A3A3A" : "#EEF0F2");
        r["CreateActionBg"] = dark ? Br("#D08A18")
            : sunset ? Grad("#FFF2A9", "#FFE5C1", "#FFD6E6")
            : Br("#E6A82C");
        r["PracticeActionBg"] = sunset ? Grad("#C9F7B8", "#DDF5C9", "#FFD8E7") : Br(dark ? "#2F6FD0" : "#457FD6");

        // 琴键色
        KeySquare  = Col(Pick("#4a4a4a", "#e2e2e6", "#FDEBFA"));
        KeyLit     = Col(Pick("#222222", "#c6c6cc", "#F5C4EA"));
        KeyLetter  = Col(Pick("#f2f2f2", "#333333", "#514552"));
        KeyDiamond = Col(Pick("#c4c4d6", "#9a9aa2", "#6F626F"));
        KeyBorder  = Col(Pick("#585858", "#c6c6d0", "#E3C5E8"));
        ListHover  = Col(Pick("#2f2f2f", "#e6eaf2", "#FBE6F5"));
        ListSel    = Col(Pick("#2d5a88", "#cfe0f5", "#F2C9EF"));

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
