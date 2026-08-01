using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>MIDI 导入对话框: 选音轨 + 自动移调对齐 C 大调(带白键率提示) + 手动八度 + 曲名。</summary>
public class MidiImportDialog : Window
{
    public List<(int key, double ms)>? ResultNotes;
    public string ResultName = "";
    public int ResultBpm = 120;

    readonly MidiImporter _importer;
    readonly List<MidiImporter.TrackInfo> _tracks;
    readonly List<CheckBox> _checks = new();
    readonly CheckBox _autoAlign;
    readonly TextBlock _alignHint;
    readonly TextBox _octaveBox;
    readonly TextBox _nameBox;

    public MidiImportDialog(MidiImporter importer, List<MidiImporter.TrackInfo> tracks, string baseName)
    {
        _importer = importer;
        _tracks = tracks;

        Title = "导入 MIDI";
        Width = 420; Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Header(baseName));

        // 音轨勾选
        panel.Children.Add(Label("选择音轨:"));
        var trackBox = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var t in tracks)
        {
            var cb = new CheckBox
            {
                Content = $"{t.Name}  ({t.NoteCount} 音符)",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 2)
            };
            cb.Checked += (_, __) => RefreshHint();
            cb.Unchecked += (_, __) => RefreshHint();
            _checks.Add(cb);
            trackBox.Children.Add(cb);
        }
        panel.Children.Add(new ScrollViewer
        {
            Content = trackBox,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        // 自动移调
        _autoAlign = new CheckBox
        {
            Content = "🎯 自动移调对齐 C 大调 (减少走音, 推荐)",
            IsChecked = true,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 6, 0, 2)
        };
        _autoAlign.Checked += (_, __) => { RefreshOctaveEnabled(); RefreshHint(); };
        _autoAlign.Unchecked += (_, __) => { RefreshOctaveEnabled(); RefreshHint(); };
        panel.Children.Add(_autoAlign);

        _alignHint = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x2a, 0xaa, 0x77)),
            FontSize = 11,
            Margin = new Thickness(22, 0, 0, 6)
        };
        panel.Children.Add(_alignHint);

        // 手动八度(自动关闭时用)
        _octaveBox = new TextBox
        {
            Text = "0", Width = 60, Height = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x2e)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50))
        };
        panel.Children.Add(Row("八度偏移:", _octaveBox));

        // 曲名
        _nameBox = new TextBox
        {
            Text = baseName, Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x2e)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            Margin = new Thickness(0, 4, 0, 0)
        };
        panel.Children.Add(Label("曲名:"));
        panel.Children.Add(_nameBox);

        panel.Children.Add(new TextBlock
        {
            Text = $"检测 BPM: {_importer.InitialBpm():0.0}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });

        // 按钮
        var ok = new Button { Content = "导入", Width = 80, Height = 30, IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += OnImport;
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        panel.Children.Add(btns);

        Content = panel;
        RefreshOctaveEnabled();
        RefreshHint();
    }

    HashSet<int> SelectedTracks()
    {
        var set = new HashSet<int>();
        for (int i = 0; i < _checks.Count; i++)
            if (_checks[i].IsChecked == true) set.Add(_tracks[i].Index);
        return set;
    }

    void RefreshOctaveEnabled() => _octaveBox.IsEnabled = _autoAlign.IsChecked != true;

    void RefreshHint()
    {
        if (_autoAlign.IsChecked != true) { _alignHint.Text = ""; return; }
        var sel = SelectedTracks();
        if (sel.Count == 0) { _alignHint.Text = "未选择音轨"; return; }
        int sh = _importer.SuggestShift(sel);
        double wr = _importer.WhiteRatioAfter(sel, sh);
        _alignHint.Text = $"自动移调 {sh:+0;-0;0} 半音, 白键率 {wr * 100:0}%";
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        var sel = SelectedTracks();
        if (sel.Count == 0) { MessageBox.Show("请至少选择一个音轨"); return; }

        int semi = _autoAlign.IsChecked == true ? _importer.SuggestShift(sel) : 0;
        int oct = _autoAlign.IsChecked == true ? 0 : (int.TryParse(_octaveBox.Text, out int o) ? o : 0);
        var notes = _importer.Convert(sel, oct, semi);
        if (notes.Count == 0) { MessageBox.Show("转换后无音符"); return; }

        ResultName = string.IsNullOrWhiteSpace(_nameBox.Text) ? "MIDI 导入" : _nameBox.Text.Trim();
        ResultBpm = (int)Math.Round(_importer.InitialBpm());
        ResultNotes = notes;
        DialogResult = true;
    }

    static TextBlock Header(string name) => new()
    {
        Text = name, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
        Margin = new Thickness(0, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis
    };

    static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
        Margin = new Thickness(0, 4, 0, 3)
    };

    static StackPanel Row(string label, UIElement field)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(field);
        return row;
    }
}
