using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>上传曲谱对话框: 标题/作者/创谱人/难度/标签/简介, 默认值取自曲谱文件首对象。</summary>
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

        var header = new TextBlock { Text = "上传到在线曲库", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)), HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock { Text = $"以 {CloudApi.Username} 身份上传", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 14) };

        var titleBox = Field(dTitle);
        var artistBox = Field(dArtist);
        var transBox = Field(dTrans);
        var diff = new ComboBox { Height = 30, ItemsSource = new[] { "★ 简单", "★★ 普通", "★★★ 中等", "★★★★ 困难", "★★★★★ 大师" }, SelectedIndex = 2 };
        var tagsBox = Field("");
        var descBox = Field("");
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x6b, 0x6b)), FontSize = 11, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };

        var upload = new Button { Content = "☁ 上传", Height = 38, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x4d, 0x8e, 0xff)), IsDefault = true, Margin = new Thickness(0, 6, 0, 0) };

        async void DoUpload()
        {
            var title = titleBox.Text.Trim();
            if (title.Length == 0) { error.Text = "曲名不能为空"; return; }
            upload.IsEnabled = false; upload.Content = "上传中..."; error.Text = "";
            var err = await CloudApi.UploadAsync(filePath, title, artistBox.Text.Trim(), transBox.Text.Trim(),
                diff.SelectedIndex + 1, tagsBox.Text.Trim(), descBox.Text.Trim());
            if (err == null) DialogResult = true;
            else { error.Text = err; upload.IsEnabled = true; upload.Content = "☁ 上传"; }
        }
        upload.Click += (_, __) => DoUpload();

        var panel = new StackPanel { Margin = new Thickness(28, 8, 28, 20) };
        panel.Children.Add(header);
        panel.Children.Add(sub);
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

    static TextBox Field(string val) => new()
    {
        Text = val, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13,
        Background = new SolidColorBrush(Color.FromRgb(0x4a, 0x4a, 0x4a)), Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
    };

    static StackPanel Labeled(string label, Control field)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa)), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) });
        p.Children.Add(field);
        return p;
    }
}
