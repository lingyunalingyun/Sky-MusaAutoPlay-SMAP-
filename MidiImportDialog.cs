using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>MIDI 导入对话框: 选音轨 + 自动移调对齐 C 大调(带白键率提示) + 手动八度 + 曲名。跟随主题、四语言、圆角控件。</summary>
public class MidiImportDialog : ChromeWindow
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

    public MidiImportDialog(MidiImporter importer, List<MidiImporter.TrackInfo> tracks, string baseName) : base(Lang.S("midi.title"), 420)
    {
        _importer = importer;
        _tracks = tracks;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = baseName, FontWeight = FontWeights.Bold, Foreground = B("TextFg"), Margin = new Thickness(0, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis });

        // 音轨勾选
        panel.Children.Add(Label(Lang.S("midi.tracks")));
        var trackBox = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var t in tracks)
        {
            var cb = new CheckBox { Content = $"{t.Name}  ({t.NoteCount} {Lang.S("midi.notes")})", IsChecked = true, Foreground = B("TextFg"), Margin = new Thickness(0, 2, 0, 2) };
            cb.Checked += (_, __) => RefreshHint();
            cb.Unchecked += (_, __) => RefreshHint();
            _checks.Add(cb);
            trackBox.Children.Add(cb);
        }
        panel.Children.Add(new ScrollViewer { Content = trackBox, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        // 自动移调
        _autoAlign = new CheckBox { Content = Lang.S("midi.autoalign"), IsChecked = true, Foreground = B("TextFg"), Margin = new Thickness(0, 6, 0, 2) };
        _autoAlign.Checked += (_, __) => { RefreshOctaveEnabled(); RefreshHint(); };
        _autoAlign.Unchecked += (_, __) => { RefreshOctaveEnabled(); RefreshHint(); };
        panel.Children.Add(_autoAlign);

        _alignHint = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x2a, 0xaa, 0x77)), FontSize = 11, Margin = new Thickness(22, 0, 0, 6) };
        panel.Children.Add(_alignHint);

        // 手动八度(自动关闭时用)
        _octaveBox = Field("0", 60);
        panel.Children.Add(Row(Lang.S("midi.octave"), _octaveBox));

        // 曲名
        _nameBox = Field(baseName, 0);
        _nameBox.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(Label(Lang.S("midi.name")));
        panel.Children.Add(_nameBox);

        panel.Children.Add(new TextBlock { Text = string.Format(Lang.S("midi.bpm"), _importer.InitialBpm().ToString("0.0")), Foreground = B("SubTextFg"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });

        // 按钮
        var ok = new Button { Content = Lang.S("midi.import"), Width = 84, Height = 34, IsDefault = true, Cursor = System.Windows.Input.Cursors.Hand, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xD0)), BorderThickness = new Thickness(0), Template = BtnTpl() };
        var cancel = new Button { Content = Lang.S("d.cancel"), Width = 84, Height = 34, IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, Foreground = B("NeutralBtnFg"), Background = B("NeutralBtnBg"), BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1), Template = BtnTpl() };
        ok.Click += OnImport;
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        panel.Children.Add(btns);

        SetBody(panel);
        RefreshOctaveEnabled();
        RefreshHint();
    }

    TextBox Field(string val, double width) => new()
    {
        Text = val, Height = 30, Width = width > 0 ? width : double.NaN, VerticalContentAlignment = VerticalAlignment.Center,
        Background = B("BoxBg"), Foreground = B("TextFg"), CaretBrush = B("TextFg"), BorderBrush = B("BoxBorder"),
        BorderThickness = new Thickness(1), Template = ChromeWindow.TextBoxTpl(),
        HorizontalAlignment = width > 0 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch
    };

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
        if (sel.Count == 0) { _alignHint.Text = Lang.S("midi.notrack"); return; }
        int sh = _importer.SuggestShift(sel);
        double wr = _importer.WhiteRatioAfter(sel, sh);
        _alignHint.Text = string.Format(Lang.S("midi.aligned"), sh.ToString("+0;-0;0"), (wr * 100).ToString("0"));
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        var sel = SelectedTracks();
        if (sel.Count == 0) { MsgBox.Info(this, Lang.S("midi.pickone")); return; }

        int semi = _autoAlign.IsChecked == true ? _importer.SuggestShift(sel) : 0;
        int oct = _autoAlign.IsChecked == true ? 0 : (int.TryParse(_octaveBox.Text, out int o) ? o : 0);
        var notes = _importer.Convert(sel, oct, semi);
        if (notes.Count == 0) { MsgBox.Info(this, Lang.S("midi.noresult")); return; }

        ResultName = string.IsNullOrWhiteSpace(_nameBox.Text) ? Lang.S("midi.defname") : _nameBox.Text.Trim();
        ResultBpm = (int)Math.Round(_importer.InitialBpm());
        ResultNotes = notes;
        DialogResult = true;
    }

    TextBlock Label(string text) => new() { Text = text, Foreground = B("TextFg"), Margin = new Thickness(0, 4, 0, 3) };

    StackPanel Row(string label, UIElement field)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, Foreground = B("TextFg"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(field);
        return row;
    }
}
