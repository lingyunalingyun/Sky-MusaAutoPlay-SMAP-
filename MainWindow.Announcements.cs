using System.Windows;
using SMAP_WPF.Announcements;

namespace SMAP_WPF;

public partial class MainWindow
{
    AnnouncementService? _announcements;
    AnnouncementWindow? _announcementWindow;
    AnnouncementNoticeWindow? _announcementNotice;
    readonly CancellationTokenSource _announcementLifetime = new();
    bool _announcementStarted, _announcementClosed, _importantOffered;

    void InitializeAnnouncements()
    {
        string path = _verificationMode ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SMAP-announcement-verification", Guid.NewGuid() + ".json") : AnnouncementReadStore.DefaultPath;
        _announcements = new(new AnnouncementClient(), new AnnouncementReadStore(path));
        _announcements.Changed += AnnouncementChanged;
        Activated += (_, _) => TryShowImportantAnnouncement();
        if (!_verificationMode) Loaded += async (_, _) =>
        {
            if (_announcementStarted) return; _announcementStarted = true;
            try { await Task.Delay(5000, _announcementLifetime.Token); if (!_announcementClosed) await _announcements.Refresh(); }
            catch (OperationCanceledException) when (_announcementClosed) { }
        };
    }
    void AnnouncementChanged()
    {
        if (_announcementClosed) return;
        AnnouncementUnreadDot.Visibility = _announcements?.HasUnread == true ? Visibility.Visible : Visibility.Collapsed;
        if (!_verificationMode) TryShowImportantAnnouncement();
    }
    void Announcements_Click(object sender, RoutedEventArgs e) => OpenAnnouncements();
    void OpenAnnouncements(long? id = null)
    {
        if (_announcements == null || _announcementClosed) return;
        if (_announcementWindow?.IsVisible == true)
        {
            _announcementWindow.Activate(); if (id.HasValue) _ = _announcementWindow.Open(id.Value); return;
        }
        var window = new AnnouncementWindow(this, _announcements, id); _announcementWindow = window;
        window.Closed += (_, _) => _announcementWindow = null; window.Show();
    }
    async void TryShowImportantAnnouncement()
    {
        if (_announcementClosed || _importantOffered || !IsVisible || WindowState == WindowState.Minimized || !IsEnabled
            || System.Windows.Interop.ComponentDispatcher.IsThreadModal || _updateReadyNotice?.IsVisible == true || _announcementWindow?.IsVisible == true
            || _announcements?.Important() is not { } item) return;
        _importantOffered = true;
        var notice = new AnnouncementNoticeWindow(this, item); _announcementNotice = notice;
        notice.ReadRequested += () => OpenAnnouncements(item.Id);
        notice.Closed += (_, _) => _announcementNotice = null; notice.Show();
        await _announcements.NoticeShown(item);
    }
    void CloseAnnouncements()
    {
        _announcementClosed = true; _announcementLifetime.Cancel(); _announcementNotice?.Close(); _announcementWindow?.Close();
        _announcements?.Dispose(); _announcementLifetime.Dispose();
    }
}
