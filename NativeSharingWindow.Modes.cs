using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed partial class NativeSharingWindow
{
    readonly SharingModeTabs _modes = new();
    readonly Grid _onlineHost = new(), _offlineHost = new();
    ScoreExportPane? _offlinePane;
    CancellationTokenSource? _lookupCancel;
    Func<Task>? _refreshCloud;
    ContextMenu? _publicationMenu;

    void InitializeSharingLayout()
    {
        var root = new Grid { Margin = new Thickness(14), Height = Math.Min(650, Math.Max(440, SystemParameters.WorkArea.Height - 110)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) }); header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(_modes); _modes.OfflineEnabled = _project != null;
        AddNavigation(S("get"), ShowCode); AddNavigation(S("myWorks"), () => _ = Run(ShowMyWorks));
        _navigation.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(_navigation, 1); header.Children.Add(_navigation); root.Children.Add(header);
        _onlineHost.RowDefinitions.Add(new RowDefinition()); _onlineHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); _onlineHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _onlineHost.Children.Add(_scroll); Grid.SetRow(_actions, 1); _onlineHost.Children.Add(_actions); Grid.SetRow(_status, 2); _onlineHost.Children.Add(_status);
        _offlineHost.Visibility = Visibility.Collapsed; _offlineHost.IsHitTestVisible = false;
        Grid.SetRow(_onlineHost, 1); Grid.SetRow(_offlineHost, 1); root.Children.Add(_onlineHost); root.Children.Add(_offlineHost);
        _modes.SelectionChanged += ChangeSharingMode; SetBody(root);
    }
    void ChangeSharingMode(int mode)
    {
        _publicationMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        CloseOnlineChoices(_onlineHost);
        if (mode == 1)
        {
            _lookupCancel?.Cancel();
            if (_offlinePane == null && _project != null)
            {
                SetOfflineSource(_project, _activeTrack == Guid.Empty ? _project.Tracks[0].Id : _activeTrack);
            }
        }
        else _offlinePane?.CloseChoices();
        var shown = mode == 1 ? _offlineHost : _onlineHost;var hidden = mode == 1 ? _onlineHost : _offlineHost;
        shown.IsHitTestVisible = true;hidden.IsHitTestVisible = false;_navigation.Visibility = mode == 1 ? Visibility.Hidden : Visibility.Visible;
        shown.Visibility = Visibility.Visible;
        if (SystemParameters.ClientAreaAnimation)
        {
            shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            var fade = new DoubleAnimation(hidden.Opacity, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => { if (_modes.SelectedIndex == mode) hidden.Visibility = Visibility.Collapsed; };
            hidden.BeginAnimation(OpacityProperty, fade);
        }
        else { shown.BeginAnimation(OpacityProperty, null);shown.Opacity = 1;hidden.Visibility = Visibility.Collapsed; }
    }
    async Task RefreshCloudSelection()
    { if (_refreshCloud != null) await _refreshCloud(); }

    static void CloseOnlineChoices(DependencyObject root)
    {
        if (root is JoinedChoiceButton choice) choice.Close(true);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) CloseOnlineChoices(VisualTreeHelper.GetChild(root, i));
    }

    void SetOfflineSource(SmapProject project, Guid activeTrack, SharingVersion? share = null)
    {
        _offlinePane?.Dispose();_offlineHost.Children.Clear();
        var body = new Grid();body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });body.RowDefinitions.Add(new RowDefinition());
        var heading = new StackPanel();heading.Children.Add(Label(project.Metadata.Name, 21));heading.Children.Add(Label(S("offlineNotice"), 12));body.Children.Add(heading);
        _offlinePane = new ScoreExportPane(this, project, activeTrack, share);
        Grid.SetRow(_offlinePane, 1);body.Children.Add(_offlinePane);_offlineHost.Children.Add(body);_modes.OfflineEnabled = true;
    }

    void ShowMatchingVersions(SharingMatches matches)
    {
        Reset(_project?.Metadata.Name ?? S("cloudFound")); _body.Children.Add(Label(S("cloudFound"), 19));_body.Children.Add(Label(S("reuseNotice"), 13));
        var card = new Grid();card.ColumnDefinitions.Add(new ColumnDefinition());card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });card.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();left.Children.Add(Label(S("versionChoice"), 13));
        var selected = new ComboBox { ItemsSource = matches.Items.Select(v => new MatchingChoice(v, v.Title + " · " + string.Format(S("version"), v.VersionNumber) + " · " + (v.PublisherName ?? v.OwnerUserId.ToString()) + " · " + S(v.Visibility == "public" ? "public" : "private"))).ToList(), DisplayMemberPath = nameof(MatchingChoice.Label), SelectedIndex = 0 };
        var picker = new JoinedChoiceButton { Height = 38, Margin = new Thickness(0, 8, 0, 8) };
        picker.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("SelectedItem.Label") { Source = selected });
        picker.Click += (_, _) => { Panel.SetZIndex(picker, 20); picker.Toggle(selected); };left.Children.Add(picker);
        var details = new StackPanel();left.Children.Add(details);card.Children.Add(left);
        var codeDetails = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var codeCard = new Border { Background = B("BoxBg"), BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18), MinHeight = 190, Child = codeDetails };
        Grid.SetColumn(codeCard, 2);card.Children.Add(codeCard);
        _body.Children.Add(new Border { Background = B("PanelBg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Margin = new Thickness(0, 12, 0, 8), Child = card });
        _body.Children.Add(Label(S("preservePublisher"), 13));
        void RenderSelection()
        {
            if (selected.SelectedItem is not MatchingChoice choice) return;var version = choice.Version;
            details.Children.Clear();codeDetails.Children.Clear();_actions.Children.Clear();
            details.Children.Add(Label(string.Format(S("version"), version.VersionNumber), 17));
            details.Children.Add(Label(string.Format(S("publisher"), version.PublisherName ?? version.OwnerUserId.ToString())));
            details.Children.Add(Label(version.Artist + " · " + version.TranscribedBy));
            var code = Label(version.FormattedCode, 30);code.Foreground = B("Accent");codeDetails.Children.Add(code);
            codeDetails.Children.Add(Label(version.Visibility == "private" ? S("private") : S("public"), 13));
            if (matches.More) details.Children.Add(Label(S("moreMatches"), 12));
            ActionButton(S("history"), () => ShowHistory(version.WorkId), target: _actions);
            ActionButton(S("copyVersion"), async () => { await _client.VersionAsync(version.VersionId, _cancel.Token);await Copy(new Uri(_client.Origin, "smap.php?v=" + version.VersionId).AbsoluteUri); }, target: _actions);
            ActionButton(S("copyCode"), async () => { await _client.VersionAsync(version.VersionId, _cancel.Token);await Copy(version.FormattedCode); }, true, _actions);
        }
        selected.SelectionChanged += (_, _) => { picker.Close(true); RenderSelection(); };RenderSelection();_status.Text = "";
    }
    sealed record MatchingChoice(SharingVersion Version, string Label);
}
