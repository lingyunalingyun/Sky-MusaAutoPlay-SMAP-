namespace SMAP_WPF;

public static partial class Lang
{
    static readonly Dictionary<string, string[]> UnifiedSharingText = new()
    {
        ["onlineMode"] = ["在线分享", "線上分享", "Online", "オンライン"],
        ["offlineMode"] = ["离线分享", "離線分享", "Offline", "オフライン"],
        ["checkingCloud"] = ["正在核对云端版本…", "正在核對雲端版本…", "Checking existing versions…", "既存の版を照合中…"],
        ["checkCloud"] = ["核对云端版本", "核對雲端版本", "Check existing versions", "既存の版を照合"],
        ["cloudFound"] = ["云端已有一致的演奏版本", "雲端已有一致的演奏版本", "A matching version is already online", "一致する演奏の版があります"],
        ["reuseNotice"] = ["直接分享已有版本，无需再次上传歌曲。", "直接分享已有版本，無需再次上傳歌曲。", "Share the existing version without uploading again.", "既存の版を共有できるため、再アップロードは不要です。"],
        ["preservePublisher"] = ["分享保留原发布者署名，不会改变作品归属。", "分享保留原發布者署名，不會改變作品歸屬。", "Sharing preserves the original publisher and ownership.", "元の公開者の名義と所有権を保持します。"],
        ["publisher"] = ["云端发布者：{0}", "雲端發布者：{0}", "Published by: {0}", "公開者：{0}"],
        ["versionChoice"] = ["选择匹配版本", "選擇匹配版本", "Matching version", "一致する版を選択"],
        ["cloudAbsent"] = ["未找到一致的云端版本，可上传所选音轨。", "未找到一致的雲端版本，可上傳選取的音軌。", "No matching version found. You can upload the selected tracks.", "一致する版がありません。選択トラックをアップロードできます。"],
        ["selectionChanged"] = ["所选音轨已变化，请重新核对云端版本。", "所選音軌已變更，請重新核對雲端版本。", "The track selection changed. Check existing versions again.", "選択トラックが変更されました。再照合してください。"],
        ["uploadShare"] = ["上传并分享", "上傳並分享", "Upload and share", "アップロードして共有"],
        ["more"] = ["更多…", "更多…", "More…", "その他…"],
        ["legacyPending"] = ["旧曲库尚待升级，暂时无法确认是否已有此曲谱；不会重复上传。可先使用离线分享。", "舊曲庫尚待升級，暫時無法確認是否已有此曲譜；不會重複上傳。可先使用離線分享。", "The legacy catalog upgrade is pending, so a duplicate cannot be ruled out. Nothing will be uploaded. Offline sharing is available.", "旧ライブラリの更新待ちのため、重複を確認できません。アップロードせず、オフライン共有を利用できます。"],
        ["offlineNotice"] = ["无需登录或联网；每页图片都保留完整工程恢复数据。", "無需登入或連線；每頁圖片都保留完整工程還原資料。", "No sign-in or network needed. Each image keeps full project recovery data.", "ログインや接続は不要です。各画像に完全な復元データを保存します。"],
        ["moreMatches"] = ["仅显示最近 20 个可访问的匹配版本。", "僅顯示最近 20 個可存取的匹配版本。", "Showing the latest 20 accessible matching versions.", "アクセス可能な最新20件の一致版を表示しています。"]
    };
}
