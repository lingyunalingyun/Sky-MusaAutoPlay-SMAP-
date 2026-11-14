namespace SMAP_WPF;

public static partial class Lang
{
    static readonly Dictionary<string, string[]> AnnouncementText = new()
    {
        ["title"] = ["公告", "公告", "Announcements", "お知らせ"],
        ["refresh"] = ["刷新", "重新整理", "Refresh", "再読込"],
        ["unread"] = ["未读", "未讀", "Unread", "未読"],
        ["important"] = ["重要", "重要", "Important", "重要"],
        ["loading"] = ["正在加载公告…", "正在載入公告…", "Loading announcements…", "お知らせを読込中…"],
        ["empty"] = ["暂无公告", "暫無公告", "No announcements yet", "お知らせはありません"],
        ["choose"] = ["选择一条公告查看详情", "選取一則公告查看詳細內容", "Choose an announcement to read", "お知らせを選んでください"],
        ["error"] = ["暂时无法加载公告，请检查网络后重试。本地功能可继续使用。", "暫時無法載入公告，請檢查網路後重試。本機功能仍可使用。", "Announcements are unavailable. Check your connection and retry. Local features remain available.", "お知らせを取得できません。接続を確認して再試行してください。ローカル機能は引き続き使えます。"],
        ["unavailable"] = ["这条公告已撤下或暂时不可用。", "這則公告已撤下或暫時無法使用。", "This announcement was withdrawn or is unavailable.", "このお知らせは公開を終了したか、現在取得できません。"],
        ["more"] = ["加载更多", "載入更多", "Load more", "もっと読む"],
        ["loaded"] = ["已加载 {0} / {1} 条", "已載入 {0} / {1} 則", "Showing {0} of {1}", "{1} 件中 {0} 件を表示"],
        ["importantNew"] = ["新的重要公告", "新的重要公告", "New important announcement", "新しい重要なお知らせ"],
        ["open"] = ["查看公告", "查看公告", "Read announcement", "お知らせを読む"],
        ["later"] = ["稍后", "稍後", "Later", "後で"],
        ["stateSaveError"] = ["公告已打开，但阅读状态暂时无法保存。", "公告已開啟，但閱讀狀態暫時無法儲存。", "The announcement opened, but its read state could not be saved.", "お知らせは表示されましたが、既読状態を保存できませんでした。"],
    };
    public static string Announcement(string key) => AnnouncementText.TryGetValue(key, out var values) ? values[(int)Current] : key;
}
