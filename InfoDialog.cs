using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>编辑曲谱元信息(曲名/作者/创谱人/封面)。确定后写回 SongDocument(封面存 Root["cover"] base64, 由 Save 落盘)。跟随主题深浅。</summary>
public class InfoDialog : ChromeWindow
{
    public InfoDialog(SongDocument doc) : base("编辑曲谱信息", 400)
    {
        var name = Field("曲名:", doc.Name);
        var author = Field("作者 (原唱/作曲):", doc.Author);   // 歌曲原作者, 非做谱者
        var trans = Field("做谱者:", doc.TranscribedBy);

        // ── 封面(可选, 软件自动压缩; 存进曲谱内嵌) ──
        byte[]? cover = null;
        try { var s = doc.Root["cover"]?.GetValue<string>(); if (!string.IsNullOrEmpty(s)) cover = Convert.FromBase64String(s); } catch { }
        var coverImg = new Image { Stretch = Stretch.UniformToFill };
        var coverHint = new TextBlock { Text = "无封面", Foreground = B("SubTextFg"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var coverGrid = new Grid();
        coverGrid.Children.Add(coverHint);
        coverGrid.Children.Add(coverImg);
        var coverBorder = new Border
        {
            Width = 84, Height = 84, CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Background = B("BoxBg"), BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1),
            Child = coverGrid
        };
        if (cover != null) { coverImg.Source = CoverUtil.FromBytes(cover); coverHint.Visibility = Visibility.Collapsed; }
        var pick = MiniBtn("选择封面");
        var clear = MiniBtn("移除");
        clear.Margin = new Thickness(8, 0, 0, 0);
        pick.Click += (_, __) =>
        {
            var od = new Microsoft.Win32.OpenFileDialog { Title = "选择封面", Filter = "图片 (*.jpg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp" };
            if (od.ShowDialog() != true) return;
            var bytes = CoverUtil.CompressToJpeg(od.FileName);
            if (bytes == null) return;
            cover = bytes; coverImg.Source = CoverUtil.FromBytes(bytes); coverHint.Visibility = Visibility.Collapsed;
        };
        clear.Click += (_, __) => { cover = null; coverImg.Source = null; coverHint.Visibility = Visibility.Visible; };
        var coverBtns = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var coverBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
        coverBtnRow.Children.Add(pick); coverBtnRow.Children.Add(clear);
        coverBtns.Children.Add(coverBtnRow);
        coverBtns.Children.Add(new TextBlock { Text = "自动压缩，最长边 512", Foreground = B("SubTextFg"), FontSize = 10, Margin = new Thickness(0, 6, 0, 0) });
        var coverRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        coverRow.Children.Add(coverBorder); coverRow.Children.Add(coverBtns);

        var ok = new Button { Content = "确定", Width = 80, Height = 30, IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, __) =>
        {
            doc.Name = name.Box.Text.Trim();          // 允许留空, 保存时补 -未命名N-
            doc.Author = author.Box.Text.Trim();
            doc.TranscribedBy = trans.Box.Text.Trim();
            if (cover != null) doc.Root["cover"] = Convert.ToBase64String(cover);   // 内嵌封面, Save 时落盘
            else doc.Root.Remove("cover");
            DialogResult = true;
        };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(name.Row);
        panel.Children.Add(author.Row);
        panel.Children.Add(trans.Row);
        panel.Children.Add(new TextBlock { Text = "封面:", Foreground = B("TextFg"), Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(coverRow);
        panel.Children.Add(btns);
        SetBody(panel);
    }

    Button MiniBtn(string text) => new()
    {
        Content = text, Height = 28, Padding = new Thickness(12, 0, 12, 0), Cursor = System.Windows.Input.Cursors.Hand,
        Background = B("NeutralBtnBg"), Foreground = B("NeutralBtnFg"), BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1)
    };

    (StackPanel Row, TextBox Box) Field(string label, string val)
    {
        var lbl = new TextBlock { Text = label, Foreground = B("TextFg"), Margin = new Thickness(0, 0, 0, 3) };
        var box = new TextBox
        {
            Text = val, Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
            Background = B("BoxBg"), Foreground = B("TextFg"), CaretBrush = B("TextFg"), BorderBrush = B("BoxBorder")
        };
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(lbl);
        row.Children.Add(box);
        return (row, box);
    }
}
