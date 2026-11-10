namespace SMAP_WPF;

public static partial class Lang
{
    static readonly Dictionary<string, string[]> ImportText = new()
    {
        ["importedCount"] = ["已导入 {0} 个曲谱。", "已匯入 {0} 個樂譜。", "Imported {0} scores.", "{0} 件の楽譜を読み込みました。"],
        ["importedPartial"] = ["已导入 {0} 个，失败 {1} 个。", "已匯入 {0} 個，失敗 {1} 個。", "Imported {0}; failed {1}.", "{0} 件を読み込み、{1} 件が失敗しました。"],
        ["title"] = ["导入", "匯入", "Import", "インポート"],
        ["onlineTitle"] = ["通过分享码获取谱面", "透過分享碼取得樂譜", "Get a score with a share code", "共有コードから楽譜を取得"],
        ["onlineText"] = ["输入收到的 12 位分享码，查看作品后再导入。", "輸入收到的 12 位分享碼，查看作品後再匯入。", "Enter a 12-character code, review the work, then import it.", "12文字の共有コードを入力し、作品を確認して読み込みます。"],
        ["codeLabel"] = ["分享码", "分享碼", "Share code", "共有コード"],
        ["codeHint"] = ["支持分组连字符与大小写输入。", "支援分組連字號與大小寫輸入。", "Hyphens and mixed case are accepted.", "ハイフン区切り・大文字小文字に対応。"],
        ["onlineNote"] = ["导入已有的公开作品无需登录。私密作品需要相应访问权限。", "匯入既有公開作品無需登入。私密作品需要相應存取權限。", "Public works can be imported without signing in. Private works require permission.", "公開作品はログイン不要です。非公開作品には閲覧権限が必要です。"],
        ["onlineAction"] = ["查看谱面", "查看樂譜", "View score", "楽譜を確認"],
        ["offlineTitle"] = ["从本机恢复完整工程", "從本機還原完整工程", "Restore a project from a local file", "ローカルファイルから復元"],
        ["offlineText"] = ["选择 .smap 工程，或带有恢复数据的 SVG / PNG 原图。", "選擇 .smap 工程，或含還原資料的 SVG / PNG 原圖。", "Choose a .smap project or an original SVG / PNG with recovery data.", " .smap または復元データ付きの SVG / PNG 原本を選択します。"],
        ["offlineNote"] = ["截图或转码后的图片可能没有恢复数据。仅识别有效恢复包，不按图片像素猜测音符。", "截圖或轉碼後的圖片可能沒有還原資料。僅辨識有效還原包，不依像素推測音符。", "Screenshots or converted images may lose recovery data. Notes are restored from embedded data.", "スクリーンショットや変換画像では復元データが失われる場合があります。埋め込みデータから復元します。"],
        ["offlineAction"] = ["选择文件并导入", "選擇檔案並匯入", "Choose files and import", "ファイルを選択して読み込む"],
        ["legacyTitle"] = ["导入已有 TXT / JSON 曲谱", "匯入既有 TXT / JSON 樂譜", "Import existing TXT / JSON scores", "TXT / JSON 楽譜を読み込む"],
        ["legacyText"] = ["继续使用原有曲谱格式，可一次选择多份文件。", "繼續使用原有樂譜格式，可一次選取多個檔案。", "Keep using your existing scores. Select multiple files at once.", "従来形式の楽譜をそのまま利用できます。複数選択に対応します。"],
        ["legacyNote"] = ["兼容原有本地曲库，导入后可在曲库中打开、播放和编辑。", "相容原有本機曲庫，匯入後可在曲庫中開啟、播放與編輯。", "Imported scores remain available in the local library for playback and editing.", "読み込み後はローカルライブラリで開き、演奏・編集できます。"],
        ["midiTitle"] = ["导入 MIDI，先试听再保存", "匯入 MIDI，先試聽再儲存", "Import MIDI, review before saving", "MIDI を確認・試聴して保存"],
        ["midiText"] = ["选择 MIDI 后，继续使用现有的智能映射与试听界面。", "選擇 MIDI 後，繼續使用既有智慧映射與試聽介面。", "Select a MIDI file to open the existing mapping and audition workflow.", "ファイルを選択後、既存の変換・試聴画面へ進みます。"],
        ["midiNote"] = ["选择声部与乐器 → 检查映射、试听 → 保存为 .smap。保留原有转换提示与确认。", "選擇聲部與樂器 → 檢查映射、試聽 → 儲存為 .smap。保留原有轉換提示與確認。", "Choose parts and instruments, review and audition the mapping, then save as .smap.", "パート・楽器の選択 → 変換確認と試聴 → .smap 保存。従来の確認手順を保持します。"],
        ["midiAction"] = ["选择 MIDI 并配置", "選擇 MIDI 並設定", "Choose MIDI and configure", "MIDI を選択して設定"],
        ["choose"] = ["尚未选择文件", "尚未選擇檔案", "No files selected", "ファイル未選択"],
        ["multiple"] = ["支持多选文件", "支援多選檔案", "Multiple files supported", "複数ファイルに対応"],
        ["offlineStatus"] = ["离线可用，无需登录。", "離線可用，無需登入。", "Works offline, no sign-in required.", "オフライン・ログイン不要。"],
        ["onlineStatus"] = ["仅在查看谱面时连接云端。", "僅在查看樂譜時連線雲端。", "Connects when you request a score.", "楽譜の取得時に接続します。"],
        ["close"] = ["关闭", "關閉", "Close", "閉じる"],
        ["resultTitle"] = ["作品预览", "作品預覽", "Review work", "作品を確認"],
        ["sample"] = ["示例作品 · 多轨长按", "範例作品 · 多軌長按", "Example work · multi-track hold", "サンプル作品 · 複数トラック"],
        ["publisher"] = ["发布者：原作者 · 版本 2", "發布者：原作者 · 版本 2", "Publisher: original author · Version 2", "公開者：元の作者 · 第2版"],
        ["resultNote"] = ["将此不可变版本保存到本地曲库，保留多轨与长按数据，可继续编辑。", "將此不可變版本儲存至本機曲庫，保留多軌與長按資料，可繼續編輯。", "Save this immutable version locally with its tracks and holds, ready for editing.", "この版をローカルへ保存します。トラックと長押し情報を保持し、編集できます。"],
        ["download"] = ["下载并导入", "下載並匯入", "Download and import", "ダウンロードして読み込む"],
        ["back"] = ["返回输入", "返回輸入", "Back to code", "コード入力に戻る"],
        ["smap"] = ["SMAP 导入", "SMAP 匯入", "SMAP import", "SMAP 読み込み"],
        ["offline"] = ["离线导入", "離線匯入", "Offline import", "オフライン"],
        ["legacy"] = ["TXT/JSON 导入", "TXT/JSON 匯入", "TXT/JSON import", "TXT/JSON"],
        ["midi"] = ["MIDI 导入", "MIDI 匯入", "MIDI import", "MIDI 読み込み"],
        ["wrongFormat"] = ["请选择当前 TAB 支持的文件格式。", "請選擇目前 TAB 支援的檔案格式。", "Choose a file format supported by this tab.", "このタブに対応する形式を選択してください。"],
    };

    public static string Import(string key) => ImportText.TryGetValue(key, out var values) ? values[(int)Current] : key;
}
