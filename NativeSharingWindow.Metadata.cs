using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SMAP_WPF.Core;
using Border = SMAP_WPF.G2Border;

namespace SMAP_WPF;

public sealed partial class NativeSharingWindow
{
    int _summaryGeneration;
    readonly SharingPresentation? _initialPresentation;

    sealed class PublicationForm
    {
        public TextBox Title { get; }
        public TextBox Artist { get; }
        public TextBox Transcriber { get; }
        public TextBox Description { get; }
        public string? Cover { get; private set; }
        public bool CoverEdited { get; private set; }
        readonly ComboBox _difficulty;
        readonly TextBox _tagInput;
        readonly List<string> _tags = [];
        readonly WrapPanel _chips = new();
        readonly Grid _tagHost = new() { Margin = new Thickness(0, 0, 0, 4) };
        readonly NativeSharingWindow _owner;

        public PublicationForm(NativeSharingWindow owner, StackPanel target, SmapProject project, SharingPresentation? presentation = null, bool editing = false)
        {
            _owner = owner;
            var initial = presentation ?? owner._sourceVersion?.Presentation ?? owner._initialPresentation;
            var legacy = project.Tracks.Select(t => t.LegacyFields).FirstOrDefault(f => f != null);
            initial ??= ReadLegacyPresentation(legacy);
            _difficulty = new ComboBox { ItemsSource = new[] { S("difficultyUnset"), S("difficulty1"), S("difficulty2"), S("difficulty3"), S("difficulty4"), S("difficulty5") }, SelectedIndex = initial?.Difficulty ?? 0 };
            if (initial != null) _tags.AddRange(initial.Tags);
            var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); top.ColumnDefinitions.Add(new ColumnDefinition());
            var coverPanel = new StackPanel { Margin = new Thickness(0, 7, 12, 0) };
            var image = new Image { Stretch = Stretch.UniformToFill };
            var coverHint = owner.Label(S(editing ? "metadataCoverHint" : "coverHint"), 12);
            var hint = owner.Label(S("noCover"), 12); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Center;
            var picture = new Grid(); picture.Children.Add(hint); picture.Children.Add(image);
            coverPanel.Children.Add(new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(9), ClipToBounds = true, Background = owner.B("BoxBg"), BorderBrush = owner.B("BoxBorder"), BorderThickness = new Thickness(1), Child = picture });
            var controls = new Grid { Margin = new Thickness(0, 8, 0, 0) }; controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var choose = owner.Neutral(S("coverChoose")); choose.Height = 30; choose.FontSize = 12; choose.Margin = new Thickness(0, 0, 5, 0); controls.Children.Add(choose);
            var remove = owner.Neutral(""); remove.Height = 30; remove.Template = BtnTpl("0"); remove.ToolTip = S("coverRemove"); System.Windows.Automation.AutomationProperties.SetName(remove, S("coverRemove"));
            remove.Content = new System.Windows.Shapes.Path { Width = 18, Height = 18, Stretch = Stretch.Uniform, Data = Geometry.Parse("M4 6H20 M9 6V3H15V6 M6 6L7 21H17L18 6 M10 10V17 M14 10V17"), Stroke = new SolidColorBrush(Color.FromRgb(230, 78, 88)), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Grid.SetColumn(remove, 1); controls.Children.Add(remove); coverPanel.Children.Add(controls); top.Children.Add(coverPanel);
            void SetCover(byte[]? bytes)
            {
                Cover = bytes == null ? null : Convert.ToBase64String(bytes);
                image.Source = CoverUtil.FromBytes(bytes); hint.Visibility = bytes == null ? Visibility.Visible : Visibility.Collapsed; remove.IsEnabled = bytes != null;
                coverHint.Text = S(editing ? "metadataCoverHint" : "coverHint");
            }
            SetCover(null);
            if (project.Metadata.Cover is { Length: > 0 } encoded)
            {
                try
                {
                    if (encoded.Length > 12 * 1024 * 1024) throw new InvalidDataException();
                    if (encoded.StartsWith("data:", StringComparison.Ordinal)) encoded = encoded[(encoded.IndexOf(',') + 1)..];
                    SetCover(SharingCover.Encode(Convert.FromBase64String(encoded)));
                }
                catch (Exception e) when (e is IOException or FormatException or ArgumentException or NotSupportedException or SharingException { Code: "INVALID_COVER" }) { coverHint.Text = S("coverInvalid"); }
            }
            choose.Click += async (_, _) =>
            {
                var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", CheckFileExists = true };
                if (dialog.ShowDialog(owner) != true) return;
                await owner.Run(async () =>
                {
                    var bytes = await Task.Run(() =>
                    {
                        if (new FileInfo(dialog.FileName).Length > 8 * 1024 * 1024) throw new SharingException("INVALID_COVER");
                        return SharingCover.Encode(File.ReadAllBytes(dialog.FileName));
                    }, owner._cancel.Token);
                    if (!owner._closed) { SetCover(bytes); CoverEdited = true; }
                });
            };
            remove.Click += (_, _) => { SetCover(null); CoverEdited = true; };
            var info = new StackPanel(); Grid.SetColumn(info, 1); top.Children.Add(info);
            Title = owner.Input(S("name"), project.Metadata.Name, target: info);
            var credits = new Grid(); credits.ColumnDefinitions.Add(new ColumnDefinition()); credits.ColumnDefinitions.Add(new ColumnDefinition());
            var singer = new StackPanel { Margin = new Thickness(0, 0, 6, 0) }; var maker = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            Artist = owner.Input(S("artist"), project.Metadata.Author, 100, target: singer); Transcriber = owner.Input(S("transcriber"), project.Metadata.TranscribedBy, 100, target: maker);
            credits.Children.Add(singer); Grid.SetColumn(maker, 1); credits.Children.Add(maker); info.Children.Add(credits); target.Children.Add(top);
            target.Children.Add(coverHint);
            var extra = new Grid(); Panel.SetZIndex(extra, 20); extra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) }); extra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); extra.ColumnDefinitions.Add(new ColumnDefinition());
            var difficultyField = new StackPanel(); var tagsField = new StackPanel(); Grid.SetColumn(tagsField, 2); extra.Children.Add(difficultyField); extra.Children.Add(tagsField); target.Children.Add(extra);
            difficultyField.Children.Add(owner.Label(S("difficulty"), 13));
            var difficulty = new JoinedChoiceButton { Height = 38, Margin = new Thickness(0, 0, 0, 6) };
            difficulty.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("SelectedItem") { Source = _difficulty });
            difficulty.Click += (_, _) => { Panel.SetZIndex(difficulty, 20); difficulty.Toggle(_difficulty); }; difficultyField.Children.Add(difficulty);
            tagsField.Children.Add(owner.Label(S("tags"), 13));
            _tagInput = new TextBox { Width = 95, Height = 28, MaxLength = 200, Foreground = owner.B("TextFg"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Template = TextBoxTpl(), ToolTip = S("tagsHint") };
            _tagInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitTags(); e.Handled = true; } };
            _tagHost.Children.Add(_tagInput);
            var tagHint = new TextBlock { Text = S("tagAdd"), Margin = new Thickness(10, 0, 0, 0), Foreground = owner.B("SubTextFg"), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            _tagInput.TextChanged += (_, _) => tagHint.Visibility = _tagInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _tagHost.Children.Add(tagHint); RenderTags();
            tagsField.Children.Add(new Border { MinHeight = 38, Padding = new Thickness(5, 4, 3, 0), CornerRadius = new CornerRadius(8), Background = owner.B("BoxBg"), BorderBrush = owner.B("BoxBorder"), BorderThickness = new Thickness(1), Child = _chips });
            Description = owner.Input(S("description"), project.Metadata.Description, 2000, true, target); Description.MinHeight = 100;
        }

        bool CommitTags()
        {
            var additions = _tagInput.Text.Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var all = _tags.Concat(additions).GroupBy(SharingPresentation.TagKey, StringComparer.Ordinal).Select(g => g.First()).ToArray();
            try { new SharingPresentation { Tags = all }.Validate(); }
            catch (InvalidDataException) { _owner._status.Text = S("tagsInvalid"); return false; }
            _tags.Clear(); _tags.AddRange(all); _tagInput.Clear(); RenderTags(); return true;
        }
        void RenderTags()
        {
            _chips.Children.Clear();
            foreach (string tag in _tags)
            {
                var chip = _owner.Neutral(""); chip.Content = new TextBlock { Text = tag + "  ×", TextTrimming = TextTrimming.CharacterEllipsis }; chip.MaxWidth = 160; chip.Height = 27; chip.FontSize = 12; chip.Margin = new Thickness(0, 0, 5, 4); chip.ToolTip = S("tagRemove") + ": " + tag;
                chip.Click += (_, _) => { _tags.Remove(tag); RenderTags(); }; _chips.Children.Add(chip);
            }
            _chips.Children.Add(_tagHost);
        }
        public SharingPresentation? Presentation()
        {
            if (!CommitTags()) throw new SharingException("INVALID_PRESENTATION");
            var result = new SharingPresentation { Difficulty = _difficulty.SelectedIndex > 0 ? _difficulty.SelectedIndex : null, Tags = _tags.ToArray(), CoverSha256 = SharingPresentation.CoverHash(Cover) };
            result.Validate(); return result.Difficulty == null && result.Tags.Length == 0 && result.CoverSha256 == null ? null : result;
        }
        public SongMetadataValues MetadataValues(int descriptionLimit, bool validate = true)
        {
            if (!CommitTags()) throw new SharingException("INVALID_METADATA");
            var values = new SongMetadataValues { Title = Title.Text.Trim(), Artist = Artist.Text.Trim(), TranscribedBy = Transcriber.Text.Trim(), Description = Description.Text.Trim(), Difficulty = _difficulty.SelectedIndex > 0 ? _difficulty.SelectedIndex : null, Tags = _tags.ToArray() };
            if (validate) values.Validate(true, descriptionLimit);
            return values;
        }
    }

    static SharingPresentation? ReadLegacyPresentation(JsonObject? fields)
    {
        try
        {
            if (fields == null) return null;
            int? difficulty = fields["difficulty"] is JsonValue value && value.TryGetValue<int>(out int d) && d is >= 1 and <= 5 ? d : null;
            var tags = fields["tags"] is JsonArray array ? array.Select(t => t?.GetValue<string>() ?? "").ToArray()
                : fields["tags"] is JsonValue text && text.TryGetValue<string>(out var joined) ? joined.Split([',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
            var presentation = new SharingPresentation { Difficulty = difficulty, Tags = tags.GroupBy(SharingPresentation.TagKey, StringComparer.Ordinal).Select(g => g.First()).ToArray() }; presentation.Validate(); return presentation;
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException) { return null; }
    }

    async Task UpdatePerformanceSummary(TextBlock target, Guid[] selected)
    {
        int generation = ++_summaryGeneration;
        if (_project == null) return;
        var snapshot = _project with { Tracks = _project.Tracks.Where(t => selected.Contains(t.Id)).Select(t => t with { Muted = false, Solo = false }).ToList() };
        if (snapshot.Tracks.Count == 0) { target.Text = S("tracksRequired"); return; }
        SharePerformanceInfo info;
        try { info = await Task.Run(() => SharePerformanceInfo.FromProject(snapshot), _cancel.Token); }
        catch (OperationCanceledException) { return; }
        if (_closed || generation != _summaryGeneration) return;
        string bpm = info.MinBpm == info.MaxBpm ? decimal.Round(info.MinBpm).ToString() : $"{decimal.Round(info.MinBpm)}–{decimal.Round(info.MaxBpm)}";
        bool reference = snapshot.Tracks.Any(t => t.LegacyFields != null);
        decimal seconds = decimal.Floor(info.DurationSeconds);
        string duration = $"{decimal.Floor(seconds / 60)}:{seconds % 60:00}";
        string meters = string.Join(" → ", info.Meters) + (info.MeterCount > info.Meters.Length ? " → …" : "");
        target.Text = $"{S(reference ? "referenceBpm" : "bpm")}: {bpm}\n{S("meter")}: {meters}\n{S("subdivision")}: 1/{info.GridSubdiv}\n{S("duration")}: {duration}";
    }
}

public static class SharingCover
{
    public static byte[] Encode(byte[] bytes)
    {
        if (bytes.Length is 0 or > 8 * 1024 * 1024) throw new SharingException("INVALID_COVER");
        try
        {
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 32_000_000) throw new SharingException("INVALID_COVER");
            double scale = Math.Min(1, 512d / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource image = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
            foreach (int quality in new[] { 80, 65, 45 })
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = quality }; encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = new MemoryStream(); encoder.Save(output);
                if (output.Length <= 128 * 1024) { var encoded = output.ToArray(); SharingPresentation.CoverHash(Convert.ToBase64String(encoded)); return encoded; }
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or FileFormatException) { throw new SharingException("INVALID_COVER"); }
        throw new SharingException("INVALID_COVER");
    }
}
