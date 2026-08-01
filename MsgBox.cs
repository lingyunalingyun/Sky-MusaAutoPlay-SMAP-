using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>统一风格的提示/确认弹框(ChromeWindow 外壳, 跟随主题), 取代系统 MessageBox。</summary>
public static class MsgBox
{
    public static void Info(Window? owner, string message, string title = "提示")
    {
        var win = Shell(owner, title, message, out var btns);
        var ok = Btn("确定"); ok.IsDefault = true; ok.IsCancel = true;
        ok.Click += (_, __) => win.Close();
        btns.Children.Add(ok);
        win.ShowDialog();
    }

    public static bool Confirm(Window? owner, string message, string title = "确认")
    {
        var win = Shell(owner, title, message, out var btns);
        bool yes = false;
        var ok = Btn("是"); ok.IsDefault = true;
        ok.Click += (_, __) => { yes = true; win.DialogResult = true; };
        var no = Btn("否"); no.IsCancel = true; no.Margin = new Thickness(10, 0, 0, 0);
        no.Click += (_, __) => win.DialogResult = false;
        btns.Children.Add(ok); btns.Children.Add(no);
        win.ShowDialog();
        return yes;
    }

    static ChromeWindow Shell(Window? owner, string title, string message, out StackPanel btns)
    {
        var win = new ChromeWindow(title, 400);
        if (owner != null) win.Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Br("TextFg"), FontSize = 14, Margin = new Thickness(0, 0, 0, 18) });
        btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(btns);
        win.SetBody(panel);
        return win;
    }

    static Button Btn(string text) => new()
    {
        Content = text, Width = 92, Height = 36, FontSize = 14, Cursor = Cursors.Hand,
        Foreground = Br("NeutralBtnFg"), Background = Br("NeutralBtnBg"),
        BorderBrush = Br("BtnBorder"), BorderThickness = new Thickness(1), Template = ChromeWindow.BtnTpl()
    };

    static Brush Br(string k) => (Brush)Application.Current.Resources[k];
}
