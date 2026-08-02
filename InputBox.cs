using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>轻量输入框 / 单选框(WPF 无内置), 暗色风格。</summary>
public static class InputBox
{
    static readonly SolidColorBrush Bg = new(Color.FromRgb(0x24, 0x24, 0x24));
    static readonly SolidColorBrush FieldBg = new(Color.FromRgb(0x2e, 0x2e, 0x2e));
    static readonly SolidColorBrush Border = new(Color.FromRgb(0x50, 0x50, 0x50));
    static readonly SolidColorBrush Label = new(Color.FromRgb(0xcc, 0xcc, 0xcc));

    /// <summary>文本输入; 取消返回 null。</summary>
    public static string? Ask(Window owner, string title, string header, string prompt)
    {
        var box = new TextBox
        {
            Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
            Background = FieldBg, Foreground = Brushes.White, BorderBrush = Border
        };
        var (win, ok) = Build(owner, title, header, prompt, box);
        string? result = null;
        ok.Click += (_, __) => { result = box.Text; win.DialogResult = true; };
        box.Focus();
        return win.ShowDialog() == true ? result : null;
    }

    /// <summary>从若干选项里单选; 取消返回 null。</summary>
    public static string? Choose(Window owner, string title, string header, string prompt, IList<string> options)
    {
        var combo = new ComboBox { Height = 26, ItemsSource = options, SelectedIndex = 0 };
        var (win, ok) = Build(owner, title, header, prompt, combo);
        string? result = null;
        ok.Click += (_, __) => { result = combo.SelectedItem as string; win.DialogResult = true; };
        return win.ShowDialog() == true ? result : null;
    }

    static (Window win, Button ok) Build(Window owner, string title, string header, string prompt, UIElement field)
    {
        var win = new ChromeWindow(title, 360) { Owner = owner };
        var ok = new Button
        {
            Content = Lang.S("d.ok"), Width = 88, Height = 34, IsDefault = true, Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xD0)), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Template = ChromeWindow.BtnTpl()
        };
        var cancel = new Button
        {
            Content = Lang.S("d.cancel"), Width = 88, Height = 34, IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
            Background = FieldBg, Foreground = Brushes.White, BorderBrush = Border, BorderThickness = new Thickness(1), Template = ChromeWindow.BtnTpl()
        };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = header, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = prompt, Foreground = Label, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(field);
        panel.Children.Add(btns);
        win.SetBody(panel);
        return (win, ok);
    }
}
