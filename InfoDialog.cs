using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>编辑曲谱元信息(曲名/作者/创谱人)。确定后写回 SongDocument。</summary>
public class InfoDialog : ChromeWindow
{
    public InfoDialog(SongDocument doc) : base("编辑曲谱信息", 400)
    {
        var name = Field("曲名:", doc.Name);
        var author = Field("作者 (原唱/作曲):", doc.Author);   // 歌曲原作者, 非做谱者
        var trans = Field("做谱者:", doc.TranscribedBy);

        var ok = new Button { Content = "确定", Width = 80, Height = 30, IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, __) =>
        {
            doc.Name = name.Box.Text.Trim();          // 允许留空, 保存时补 -未命名N-
            doc.Author = author.Box.Text.Trim();
            doc.TranscribedBy = trans.Box.Text.Trim();
            DialogResult = true;
        };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(name.Row);
        panel.Children.Add(author.Row);
        panel.Children.Add(trans.Row);
        panel.Children.Add(btns);
        SetBody(panel);
    }

    static (StackPanel Row, TextBox Box) Field(string label, string val)
    {
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            Margin = new Thickness(0, 0, 0, 3)
        };
        var box = new TextBox
        {
            Text = val,
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x2e)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50))
        };
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(lbl);
        row.Children.Add(box);
        return (row, box);
    }
}
