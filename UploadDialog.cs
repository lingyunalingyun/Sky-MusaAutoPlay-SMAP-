using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>上传曲谱对话框: 封面(必填,自动压缩)/标题/作者/创谱人/难度/标签/简介, 默认值取自曲谱文件首对象。跟随主题深浅。</summary>
public class UploadDialog : ChromeWindow
{
    public UploadDialog(Window owner, string filePath, string displayName) : base("上传曲谱 — " + displayName, 400)
    {
        Owner = owner;

        // 从曲谱文件读默认曲名/作者/创谱人
        string dTitle = displayName, dArtist = "", dTrans = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var o = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0] : doc.RootElement;
            if (o.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) dTitle = n.GetString() ?? dTitle;
            if (o.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.String) dArtist = a.GetString() ?? "";
            if (o.TryGetProperty("transcribedBy", out var t) && t.ValueKind == JsonValueKind.String) dTrans = t.GetString() ?? "";
        }
        catch { /* 用默认 */ }

        var header = new TextBlock { Text = "上传到在线曲库", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = B("TextFg"), HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock { Text = $"以 {CloudApi.Username} 身份上传", FontSize = 11, Foreground = B("SubTextFg"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 14) };

        var titleBox = Field(dTitle);
        var artistBox = Field(dArtist);
        var transBox = Field(dTrans);
        var diff = new ComboBox { Height = 30, ItemsSource = new[] { "★ 简单", "★★ 普通", "★★★ 中等", "★★★★ 困难", "★★★★★ 大师" }, SelectedIndex = 2 };
        var tagsBox = Field("");
        var descBox = Field("");
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x6b, 0x6b)), FontSize = 11, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };

        // ── 封面(必填, 软件自动压缩)──
        byte[]? cover = CoverUtil.ReadEmbedded(filePath);   // 默认读曲谱已内嵌的封面
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
        var pickBtn = MiniBtn("选择封面");
        var clearBtn = MiniBtn("移除");
        clearBtn.Margin = new Thickness(8, 0, 0, 0);
        pickBtn.Click += (_, __) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择封面", Filter = "图片 (*.jpg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            var bytes = CoverUtil.CompressToJpeg(dlg.FileName);
            if (bytes == null) { error.Text = "封面读取失败"; return; }
            cover = bytes; coverImg.Source = CoverUtil.FromBytes(bytes); coverHint.Visibility = Visibility.Collapsed; error.Text = "";
        };
        clearBtn.Click += (_, __) => { cover = null; coverImg.Source = null; coverHint.Visibility = Visibility.Visible; };
        var coverBtns = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var coverBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
        coverBtnRow.Children.Add(pickBtn); coverBtnRow.Children.Add(clearBtn);
        coverBtns.Children.Add(coverBtnRow);
        coverBtns.Children.Add(new TextBlock { Text = "自动压缩，最长边 512", Foreground = B("SubTextFg"), FontSize = 10, Margin = new Thickness(0, 6, 0, 0) });
        var coverRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        coverRow.Children.Add(coverBorder); coverRow.Children.Add(coverBtns);

        var upload = new Button { Content = "☁ 上传", Height = 38, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x4d, 0x8e, 0xff)), IsDefault = true, Margin = new Thickness(0, 6, 0, 0) };

        async void DoUpload()
        {
            var title = titleBox.Text.Trim();
            if (title.Length == 0) { error.Text = "曲名不能为空"; return; }
            if (cover == null) { error.Text = "请先选择封面"; return; }   // 本地无封面时必须传封面
            upload.IsEnabled = false; upload.Content = "上传中..."; error.Text = "";
            var err = await CloudApi.UploadAsync(filePath, title, artistBox.Text.Trim(), transBox.Text.Trim(),
                diff.SelectedIndex + 1, tagsBox.Text.Trim(), descBox.Text.Trim(), cover);
            if (err == null)
            {
                if (cover != null) CoverUtil.WriteEmbedded(filePath, cover);   // 本地曲谱内嵌封面, 播放时也显示
                DialogResult = true;
            }
            else { error.Text = err; upload.IsEnabled = true; upload.Content = "☁ 上传"; }
        }
        upload.Click += (_, __) => DoUpload();

        var panel = new StackPanel { Margin = new Thickness(28, 8, 28, 20) };
        panel.Children.Add(header);
        panel.Children.Add(sub);
        panel.Children.Add(new TextBlock { Text = "封面（必填）", Foreground = B("SubTextFg"), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(coverRow);
        panel.Children.Add(Labeled("曲名", titleBox));
        panel.Children.Add(Labeled("原唱 / 作曲", artistBox));
        panel.Children.Add(Labeled("创谱人", transBox));
        panel.Children.Add(Labeled("难度", diff));
        panel.Children.Add(Labeled("标签（逗号分隔）", tagsBox));
        panel.Children.Add(Labeled("简介（可选）", descBox));
        panel.Children.Add(error);
        panel.Children.Add(upload);
        SetBody(panel);
    }

    Button MiniBtn(string text) => new()
    {
        Content = text, Height = 30, Padding = new Thickness(14, 0, 14, 0), Cursor = System.Windows.Input.Cursors.Hand,
        Background = B("NeutralBtnBg"), Foreground = B("NeutralBtnFg"), BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1)
    };

    TextBox Field(string val) => new()
    {
        Text = val, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13,
        Background = B("BoxBg"), Foreground = B("TextFg"), CaretBrush = B("TextFg"), BorderBrush = B("BoxBorder")
    };

    StackPanel Labeled(string label, Control field)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, Foreground = B("SubTextFg"), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) });
        p.Children.Add(field);
        return p;
    }
}
