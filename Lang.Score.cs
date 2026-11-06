namespace SMAP_WPF;

public static partial class Lang
{
    static void AddScoreTranslations()
    {
        void A(string k, string zh, string tw, string en, string ja) => T["score." + k] = new[] { zh, tw, en, ja };
        A("title", "图片谱导出…", "圖片譜匯出…", "Export sheet images…", "画像楽譜を書き出す…");
        A("tracks", "选择音轨", "選擇音軌", "Select tracks", "トラックを選択");
        A("track", "音轨", "音軌", "Track", "トラック");
        A("orientation", "方向", "方向", "Orientation", "向き");
        A("portrait", "竖版 9:16 · 1260×2240", "直式 9:16 · 1260×2240", "Portrait 9:16 · 1260×2240", "縦 9:16 · 1260×2240");
        A("landscape", "横版 16:9 · 2240×1260", "橫式 16:9 · 2240×1260", "Landscape 16:9 · 2240×1260", "横 16:9 · 2240×1260");
        A("format", "格式", "格式", "Format", "形式");
        A("includeMidi", "包含完整原始 MIDI（可能含未选声部）", "包含完整原始 MIDI（可能含未選聲部）", "Include complete original MIDI (may contain unselected voices)", "元のMIDI全体を含める（未選択声部を含む場合あり）");
        A("dataNotice", "每页包含所选音轨的恢复数据。转码可能移除数据，请保留导出的 project.smap。", "每頁包含所選音軌的復原資料。轉碼可能移除資料，請保留匯出的 project.smap。", "Each page contains the selected tracks. Re-encoding may remove the data; keep project.smap.", "各ページに選択トラックの復元データを含みます。再圧縮で失われる場合があるためproject.smapも保存してください。");
        A("preview", "生成预览", "產生預覽", "Generate preview", "プレビューを生成");
        A("export", "导出到文件夹", "匯出至資料夾", "Export to folder", "フォルダーに書き出す");
        A("folder", "选择导出位置", "選擇匯出位置", "Choose export location", "保存先を選択");
        A("choose", "选择音轨与方向，然后生成预览。", "選擇音軌與方向，再產生預覽。", "Select tracks and orientation, then generate a preview.", "トラックと向きを選び、プレビューを生成してください。");
        A("working", "正在处理…关闭窗口可取消。", "處理中…關閉視窗可取消。", "Working… Close the window to cancel.", "処理中…ウィンドウを閉じると中止します。");
        A("ready", "共 {0} 页。", "共 {0} 頁。", "{0} pages.", "全{0}ページ。");
        A("exported", "导出完成", "匯出完成", "Export complete", "書き出し完了");
        A("failed", "处理失败：", "處理失敗：", "Failed:", "失敗：");
        A("imageFiles", "可恢复图片谱", "可恢復圖片譜", "Recoverable sheet images", "復元可能な画像楽譜");
        A("transcriber", "制谱人", "製譜者", "Transcribed by", "採譜者");
        A("notSet", "未填写", "未填寫", "Not specified", "未入力");
        A("positionOnly", "位置谱 · 音高未确认", "位置譜 · 音高未確認", "Key positions · pitch unverified", "キー位置譜・音高未確認");
        A("multiTrack", "多轨谱", "多軌譜", "Multiple tracks", "複数トラック");
        A("beats", "拍", "拍", "beats", "拍");
        A("freeRhythm", "自由节奏", "自由節奏", "Free rhythm", "自由リズム");
        A("ScoreReadableRhythm", "谱面按常见分拍显示；原始时值完整保留在恢复数据中。", "譜面以常見分拍顯示；原始時值完整保留於復原資料中。", "Notation uses common beat subdivisions; recovery data retains original timing.", "譜面は一般的な分拍で表示し、元の時刻は復元データに保持します。");
        A("ScoreFreeRhythm", "部分时值无法归入常见分拍，已标为自由节奏。", "部分時值無法歸入常見分拍，已標為自由節奏。", "Some timings do not match common subdivisions and are marked as free rhythm.", "一般的な分拍に合わない位置は自由リズムと表示します。");
        A("ScoreInvalidTracks", "请至少选择一个有效音轨。", "請至少選擇一個有效音軌。", "Select at least one valid track.", "有効なトラックを選択してください。");
        A("ScoreUnavailableKey", "所选乐器缺少谱中使用的琴键，请检查轨道配置。", "所選樂器缺少譜中使用的琴鍵，請檢查音軌設定。", "The instrument lacks a required key. Check track settings.", "必要なキーがありません。楽器設定を確認してください。");
        A("ScoreUnknownCapability", "包含能力未确认的轨道，按位置谱输出。", "包含能力未確認的音軌，以位置譜輸出。", "Some instrument capabilities are unverified; key positions are shown.", "楽器の能力が未確認のためキー位置を表示します。");
        A("ScoreIncludesOriginalMidi", "包含完整原始 MIDI 和转换来源。", "包含完整原始 MIDI 和轉換來源。", "Includes complete original MIDI and conversion data.", "元のMIDI全体と変換情報を含みます。");
        A("ScoreTooLarge", "谱面超过页数或密度上限，请减少所选轨道。", "譜面超過頁數或密度上限，請減少所選音軌。", "Score exceeds the page or density limit. Select fewer tracks.", "ページ数または密度の上限です。トラックを減らしてください。");
        A("ScoreNoData", "图片没有恢复数据，可能已被转码移除。", "圖片沒有復原資料，可能已被轉碼移除。", "No recoverable data. Re-encoding may have removed it.", "復元データがありません。再圧縮で削除された可能性があります。");
        A("ScoreIntegrity", "图片谱完整性校验失败。", "圖片譜完整性驗證失敗。", "Sheet image integrity check failed.", "整合性検証に失敗しました。");
        A("ScoreBadImage", "图片谱无效或版本不受支持。", "圖片譜無效或版本不支援。", "Invalid or unsupported sheet image.", "無効または未対応の画像楽譜です。");
        A("ScoreImageTooLarge", "图片或数据超出安全大小限制。", "圖片或資料超過安全大小限制。", "Image or data exceeds the size limit.", "画像またはデータがサイズ制限を超えます。");
    }
}
