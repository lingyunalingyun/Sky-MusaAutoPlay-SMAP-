namespace SMAP_WPF;

public static partial class Lang
{
    static readonly Dictionary<string, string[]> UpdateText = new()
    {
        ["title"] = ["下载与更新", "下載與更新", "Downloads & updates", "ダウンロードと更新"],
        ["idle"] = ["自动检查更新", "自動檢查更新", "Check for updates automatically", "更新を自動確認"],
        ["checking"] = ["正在检查更新…", "正在檢查更新…", "Checking for updates…", "更新を確認中…"],
        ["planning"] = ["正在核对本地内容…", "正在核對本機內容…", "Checking local content…", "既存データを確認中…"],
        ["downloading"] = ["后台下载中", "背景下載中", "Downloading in background", "バックグラウンドで取得中"],
        ["paused"] = ["下载已暂停", "下載已暫停", "Download paused", "ダウンロードを一時停止中"],
        ["reconstructing"] = ["正在准备更新…", "正在準備更新…", "Preparing update…", "更新を準備中…"],
        ["ready"] = ["更新就绪", "更新就緒", "Update ready", "更新の準備完了"],
        ["current"] = ["当前已是最新版本", "目前已是最新版本", "You are up to date", "最新バージョンです"],
        ["error"] = ["暂时无法完成更新，可重试", "暫時無法完成更新，可重試", "Update interrupted. You can retry.", "更新を中断しました。再試行できます。"],
        ["found"] = ["发现新版，正在后台下载", "發現新版，正在背景下載", "Update found. Downloading in background.", "更新を検出。バックグラウンドで取得中"],
        ["continueUse"] = ["可以继续演奏、编辑和分享。", "可以繼續演奏、編輯和分享。", "Keep playing, editing and sharing.", "演奏・編集・共有を続けられます。"],
        ["download"] = ["下载进度", "下載進度", "Download", "ダウンロード"],
        ["local"] = ["本地准备", "本機準備", "Local preparation", "ローカル処理"],
        ["reuse"] = ["复用本地内容", "重用本機內容", "Local content reused", "既存データの再利用"],
        ["pause"] = ["暂停下载", "暫停下載", "Pause download", "一時停止"],
        ["resume"] = ["继续下载", "繼續下載", "Resume download", "ダウンロードを再開"],
        ["retry"] = ["重试", "重試", "Retry", "再試行"],
        ["check"] = ["检查更新", "檢查更新", "Check for updates", "更新を確認"],
        ["stableChannel"] = ["正式版本", "正式版本", "Stable", "正式版"],
        ["testingChannel"] = ["测试版本", "測試版本", "Testing", "テスト版"],
        ["channelHint"] = ["更新通道：正式版本只接收正式发布，测试版本接收已签名的预发布。切换通道不会降级。", "更新通道：正式版本只接收正式發佈，測試版本接收已簽署的預先發佈。切換不會降級。", "Update channel: Stable receives final releases; Testing receives signed prereleases. Switching never downgrades.", "更新チャンネル：正式版は正式リリース、テスト版は署名済み先行リリースを取得します。切替でダウングレードしません。"],
        ["channelSaveError"] = ["无法保存更新通道，已保留原选择。", "無法儲存更新通道，已保留原選擇。", "Could not save the update channel. Your previous choice is retained.", "更新チャンネルを保存できませんでした。前の選択を保持します。"],
        ["channelBusy"] = ["请先暂停更新，再切换通道。", "請先暫停更新，再切換通道。", "Pause the update before switching channels.", "チャンネルを切り替える前に更新を一時停止してください。"],
        ["restart"] = ["保存并重启", "儲存並重新啟動", "Save & restart", "保存して再起動"],
        ["later"] = ["稍后", "稍後", "Later", "後で"],
        ["readyBody"] = ["新版本已下载并校验完成。\n是否保存工程并重新启动？", "新版本已下載並驗證完成。\n是否儲存專案並重新啟動？", "The update has been downloaded and verified.\nSave your work and restart now?", "更新の取得と検証が完了しました。\nプロジェクトを保存して再起動しますか？"],
        ["preserved"] = ["退出前不会替换程序，歌曲和设置保持不变。", "退出前不會替換程式，歌曲及設定保持不變。", "Files change only after exit. Songs and settings are preserved.", "終了後に適用します。楽曲と設定を保持します。"],
        ["handoffError"] = ["暂时无法启动更新，已保留当前程序和下载内容。", "暫時無法啟動更新，已保留目前程式及下載內容。", "Could not start the update. Your current app and downloaded data are retained.", "更新を開始できませんでした。現在のアプリと取得済みデータを保持します。"],
        ["startupBusy"] = ["更新正在处理，或上次更新尚待恢复。请稍后启动；歌曲和设置不会被更新工具覆盖。", "更新正在處理，或上次更新尚待還原。請稍後啟動；歌曲及設定不會被更新工具覆寫。", "An update or recovery is still in progress. Try starting again shortly. The updater preserves your songs and settings.", "更新または復元を処理中です。しばらくしてから起動してください。楽曲と設定は保持されます。"],
    };
    public static string Update(string key) => UpdateText.TryGetValue(key, out var values) ? values[(int)Current] : key;
}
