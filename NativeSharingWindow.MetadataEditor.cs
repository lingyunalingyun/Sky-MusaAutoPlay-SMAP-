using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SMAP_WPF.Core;
using Border = SMAP_WPF.G2Border;

namespace SMAP_WPF;

public sealed partial class NativeSharingWindow
{
    SongMetadataProfile? _metadataProfile;
    PublicationForm? _metadataForm;
    string? _metadataPendingSignature;
    Guid _metadataRequestKey;
    public bool MetadataSaved { get; private set; }

    void InitializeMetadataLayout()
    {
        var root = new Grid { Margin = new Thickness(14), Height = Math.Min(650, Math.Max(440, SystemParameters.WorkArea.Height - 110)) };
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_scroll); Grid.SetRow(_actions, 1); root.Children.Add(_actions); Grid.SetRow(_status, 2); root.Children.Add(_status); SetBody(root);
    }
    async Task ShowMetadataEditor(SongMetadataProfile profile)
    {
        if (_closed) return;
        if (!profile.CanEdit) throw new SharingException("FORBIDDEN", 403);
        byte[]? cover = null; bool coverFailed = false;
        try { cover = await _client.MetadataCoverAsync(profile, _cancel.Token); }
        catch (Exception e) when (e is SharingException or System.Net.Http.HttpRequestException) { coverFailed = true; }
        if (_closed) return;
        _metadataProfile = profile; Reset(S("metadataEdit")); _metadataForm = null;
        _body.Children.Add(Label(S("metadataNotice"), 13));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel(); var right = new StackPanel();
        foreach (var pair in new[] { (left, 0), (right, 2) }) { var card = new Border { Padding = new Thickness(14), Background = B("PanelBg"), CornerRadius = new CornerRadius(10), Child = pair.Item1 }; Grid.SetColumn(card, pair.Item2); columns.Children.Add(card); }
        _body.Children.Add(columns);
        var values = profile.Metadata;
        var project = new SmapProject { Metadata = new() { Name = values.Title, Author = values.Artist, TranscribedBy = values.TranscribedBy, Description = values.Description, Cover = cover == null ? null : Convert.ToBase64String(cover) } };
        var form = new PublicationForm(this, left, project, new() { Difficulty = values.Difficulty, Tags = values.Tags }, true);
        form.Description.MaxLength = profile.DescriptionLimit; _metadataForm = form;
        right.Children.Add(Label(S("metadataPublished"), 16));
        if (profile.ShareCode is { Length: 12 } code)
        { var label = Label(string.Join('-', Enumerable.Range(0, 3).Select(i => code.Substring(i * 4, 4))), 25); label.Foreground = B("PracticeActionBg"); right.Children.Add(label); right.Children.Add(Label(string.Format(S("version"), profile.VersionNumber), 13)); }
        else right.Children.Add(Label(S("metadataLegacy") + " #" + profile.Id));
        right.Children.Add(Label(S("metadataAuthority"), 14)); right.Children.Add(Label(S(profile.Authority == "manager" ? "metadataManager" : "metadataPublisher"), 13));
        right.Children.Add(Label(S("performanceInfo"), 14)); right.Children.Add(Label(MetadataTiming(profile.Timing), 13));
        right.Children.Add(Label(S("metadataHistory"), 14)); right.Children.Add(Label(string.Format(S("metadataRevision"), profile.Revision), 13));
        ActionButton(S("metadataHistory"), async () => { if (ConfirmMetadataDiscard()) await ShowMetadataHistory(profile); }, target: right);
        ActionButton(S("metadataSave"), async () =>
        {
            CloseOnlineChoices(_body);
            var editable = form.MetadataValues(profile.DescriptionLimit); string action = !form.CoverEdited ? "keep" : form.Cover == null ? "remove" : "replace";
            string signature = JsonSerializer.Serialize(new { profile.Kind, profile.Id, profile.Revision, Values = editable.Editable(), Action = action, Cover = action == "replace" ? form.Cover : null }, NativeSharingClient.Json);
            if (_metadataPendingSignature != signature) { _metadataPendingSignature = signature; _metadataRequestKey = Guid.NewGuid(); }
            var saved = await _client.SaveMetadataAsync(profile, editable, action, form.Cover, _metadataRequestKey, _cancel.Token);
            MetadataSaved = true; _metadataPendingSignature = null; await ShowMetadataEditor(saved); _status.Text = S("metadataSaved");
        }, true, _actions);
        ActionButton(S("metadataReload"), async () => { if (ConfirmMetadataDiscard()) { await ShowMetadataEditor(await _client.MetadataAsync(new(profile.Kind, profile.Id), _cancel.Token)); _metadataPendingSignature = null; } }, target: _actions);
        var cancel = Neutral(S("cancel")); cancel.MinWidth = 160; cancel.Margin = new Thickness(6, 8, 0, 0);
        cancel.Click += (_, _) => Close(); _actions.Children.Add(cancel);
        if (coverFailed) _status.Text = S("metadataCoverFailed");
    }
    static string MetadataTiming(SongMetadataTiming? timing)
    {
        if (timing == null) return S("metadataTimingMissing");
        string bpm = timing.Bpm == null ? "—" : decimal.Round(timing.Bpm.Value).ToString();
        if (timing.MaxBpm != null && timing.MaxBpm != timing.Bpm) bpm += "–" + decimal.Round(timing.MaxBpm.Value);
        decimal? seconds = timing.DurationSeconds == null ? null : decimal.Floor(timing.DurationSeconds.Value);
        string duration = seconds == null ? "—" : $"{decimal.Floor(seconds.Value / 60)}:{seconds.Value % 60:00}";
        return S("referenceBpm") + ": " + bpm + "\n" + S("meter") + ": " + (timing.Meters.Length == 0 ? "—" : string.Join(" → ", timing.Meters)) + "\n" + S("duration") + ": " + duration;
    }
    bool MetadataDirty()
    {
        if (_metadataForm == null || _metadataProfile == null) return false;
        try { return _metadataForm.CoverEdited || JsonSerializer.Serialize(_metadataForm.MetadataValues(_metadataProfile.DescriptionLimit, false).Editable(), NativeSharingClient.Json) != JsonSerializer.Serialize(_metadataProfile.Metadata.Editable(), NativeSharingClient.Json); }
        catch (SharingException) { return true; }
    }
    bool ConfirmMetadataDiscard() => !MetadataDirty() || MsgBox.Confirm(this, S("metadataDiscard"), S("metadataEdit"));
    void MetadataEditorClosing(object? sender, CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        if (!ConfirmMetadataDiscard()) e.Cancel = true;
    }
    async Task ShowMetadataHistory(SongMetadataProfile profile, int? before = null)
    {
        var history = await _client.MetadataHistoryAsync(new(profile.Kind, profile.Id), before, _cancel.Token); if (_closed) return;
        _metadataForm = null; Reset(S("metadataHistory"));
        if (history.Items.Count == 0) _body.Children.Add(Label(S("metadataNoHistory")));
        foreach (var item in history.Items)
        {
            _body.Children.Add(Label(string.Format(S("metadataRevision"), item.Revision) + " · " + item.ActorName, 16));
            _body.Children.Add(Label(DateTimeOffset.FromUnixTimeSeconds(item.CreatedAt).ToLocalTime().ToString("g") + " · " + S(item.Authority == "manager" ? "metadataManager" : "metadataPublisher"), 12));
        }
        ActionButton(S("metadataBack"), async () => await ShowMetadataEditor(await _client.MetadataAsync(new(profile.Kind, profile.Id), _cancel.Token)), target: _actions);
        if (history.NextBefore != null) ActionButton(S("loadMore"), () => ShowMetadataHistory(profile, history.NextBefore));
    }
    void EditPublishedMetadata(SongMetadataTarget target, Action refreshed)
    {
        var editor = new NativeSharingWindow(this, client: _client, user: _user, metadataTarget: target);
        editor.ShowDialog();
        if (editor.MetadataSaved && !_closed) refreshed();
    }

    async Task LoadResultMetadata(SharingVersion version, StackPanel host, TextBlock title, TextBlock credits, TextBlock description, StackPanel presentation)
    {
        try
        {
            if (!await _client.MetadataSupportedAsync(_cancel.Token)) return;
            var profile = await _client.MetadataAsync(new("work", version.WorkId.ToString()), _cancel.Token);
            if (_closed || !ReferenceEquals(host, _body)) return;
            title.Text = profile.Metadata.Title; credits.Text = profile.Metadata.Artist + (profile.Metadata.TranscribedBy.Length == 0 ? "" : " · " + profile.Metadata.TranscribedBy);
            description.Text = profile.Metadata.Description; presentation.Children.Clear();
            if (profile.Metadata.Difficulty is { } difficulty) presentation.Children.Add(Label(S("difficulty") + ": " + S("difficulty" + difficulty), 13));
            if (profile.Metadata.Tags.Length > 0) presentation.Children.Add(Label(string.Join(" · ", profile.Metadata.Tags), 13));
            if (profile.CanEdit)
            {
                var edit = Neutral(S("metadataEdit")); edit.Margin = new Thickness(0, 8, 0, 8);
                edit.Click += (_, _) => EditPublishedMetadata(new("work", version.WorkId.ToString()), () => ShowResult(version));
                host.Children.Insert(host.Children.IndexOf(presentation) + 1, edit);
            }
        }
        catch (Exception e) when (e is SharingException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        { if (!_closed && ReferenceEquals(host, _body)) _status.Text = S("metadataLoadFailed"); }
    }
}
