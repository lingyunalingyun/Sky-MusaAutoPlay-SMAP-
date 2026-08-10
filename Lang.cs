using System;
using System.Collections.Generic;
using System.IO;

namespace SMAP_WPF;

public enum AppLang { ZhCN, ZhTW, En, Ja }

/// <summary>多语言: 简体/繁体/英文/日文。key→4语言, 运行时切+持久化。动态状态消息暂未纳入(阶段二)。</summary>
public static class Lang
{
    public static AppLang Current { get; private set; } = AppLang.ZhCN;

    /// <summary>菜单里显示的语言原生名。</summary>
    public static readonly string[] Names = { "简体中文", "繁體中文", "English", "日本語" };

    static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "lang.txt");

    // key → [简, 繁, En, 日]
    static readonly Dictionary<string, string[]> T = new()
    {
        ["app.title"]    = new[] { "光遇-Musa 自动演奏", "光遇-Musa 自動演奏", "Sky-Musa Auto Play", "Sky-Musa 自動演奏" },
        ["nosong"]       = new[] { "当前未选定曲目", "當前未選定曲目", "No song selected", "曲が未選択" },
        ["countdown"]    = new[] { "倒计时:", "倒數計時:", "Countdown:", "カウントダウン:" },
        ["info.notes"]   = new[] { "音符数", "音符數", "Notes", "音符数" },
        ["status.ready"] = new[] { "状态: 准备中", "狀態: 準備中", "Status: Ready", "状態: 準備完了" },

        ["btn.start"]    = new[] { "▶ 开始 (F1)", "▶ 開始 (F1)", "▶ Start (F1)", "▶ 開始 (F1)" },
        ["btn.pause"]    = new[] { "⏸ 暂停 (F2)", "⏸ 暫停 (F2)", "⏸ Pause (F2)", "⏸ 一時停止 (F2)" },
        ["btn.preview"]  = new[] { "🎧 试听", "🎧 試聽", "🎧 Preview", "🎧 試聴" },
        ["btn.edit"]     = new[] { "✏ 编辑", "✏ 編輯", "✏ Edit", "✏ 編集" },
        ["btn.create"]   = new[] { "➕ 创建", "➕ 創建", "➕ Create", "➕ 新規" },
        ["btn.cloud"]    = new[] { "☁ 云端", "☁ 雲端", "☁ Cloud", "☁ クラウド" },
        ["btn.import"]   = new[] { "📁 导入", "📁 匯入", "📁 Import", "📁 インポート" },
        ["btn.refresh"]  = new[] { "🔄 刷新", "🔄 重新整理", "🔄 Refresh", "🔄 更新" },
        ["btn.login"]    = new[] { "🔑 登录", "🔑 登入", "🔑 Login", "🔑 ログイン" },

        ["lib.header"]   = new[] { "本地曲库 (右键收藏/加标签)", "本地曲庫 (右鍵收藏/加標籤)", "Local Library (right-click to favorite/tag)", "ローカル曲庫 (右クリックでお気に入り/タグ)" },
        ["filter.all"]   = new[] { "全部", "全部", "All", "すべて" },
        ["filter.fav"]   = new[] { "⭐ 仅收藏", "⭐ 僅收藏", "⭐ Favorites", "⭐ お気に入り" },
        ["sort.az"]      = new[] { "名称 A-Z", "名稱 A-Z", "Name A-Z", "名前 A-Z" },
        ["sort.za"]      = new[] { "名称 Z-A", "名稱 Z-A", "Name Z-A", "名前 Z-A" },
        ["sort.fav"]     = new[] { "收藏优先", "收藏優先", "Favorites First", "お気に入り優先" },

        ["keys.header"]  = new[] { "🎹 光遇按键映射 (3×5)", "🎹 光遇按鍵映射 (3×5)", "🎹 Sky Key Mapping (3×5)", "🎹 キーマッピング (3×5)" },
        ["keys.hint"]    = new[] { "点单元格 → 按键盘任意键 重映射 · 播放时同步亮起", "點單元格 → 按鍵盤任意鍵 重映射 · 播放時同步亮起", "Click a cell → press any key to remap · lights up on play", "セルをクリック → 任意のキーで再割当 · 再生時に点灯" },
        ["keys.edit"]    = new[] { "🎹 编辑按键映射", "🎹 編輯按鍵映射", "🎹 Edit Key Mapping", "🎹 キー割当を編集" },
        ["keys.save"]    = new[] { "💾 保存按键映射", "💾 儲存按鍵映射", "💾 Save Key Mapping", "💾 キー割当を保存" },

        ["cave"]         = new[] { "洞穴音效", "洞穴音效", "Cave Reverb", "洞窟エコー" },
        ["on"]           = new[] { "开", "開", "On", "オン" },
        ["off"]          = new[] { "关", "關", "Off", "オフ" },
        ["instrument"]   = new[] { "音色", "音色", "Instrument", "音色" },
        ["pitch"]        = new[] { "音高", "音高", "Pitch", "ピッチ" },
        ["tip.pitch"]    = new[] { "音高·每乐器独立移调", "音高·每樂器獨立移調", "Pitch · per-instrument transpose", "音高·楽器ごとの移調" },
        ["set.pitch"]      = new[] { "音调:", "音調:", "Pitch:", "音程:" },
        ["set.pitchReset"] = new[] { "恢复默认音调", "恢復預設音調", "Reset to default", "既定に戻す" },
        ["t.pitchReset"]   = new[] { "已恢复默认音调", "已恢復預設音調", "Pitch reset to default", "音程を既定に戻しました" },
        ["speed.random"]   = new[] { "随机", "隨機", "Random", "ランダム" },
        ["theme"]        = new[] { "主题", "主題", "Theme", "テーマ" },
        ["theme.dark"]   = new[] { "深色模式", "深色模式", "Dark", "ダーク" },
        ["theme.light"]  = new[] { "浅色模式", "淺色模式", "Light", "ライト" },
        ["about"]        = new[] { "软件信息", "軟體資訊", "About", "ソフト情報" },

        ["menu.fav"]     = new[] { "收藏", "收藏", "Favorite", "お気に入り" },
        ["menu.addtag"]  = new[] { "添加标签", "添加標籤", "Add Tag", "タグ追加" },
        ["menu.rmtag"]   = new[] { "移除标签", "移除標籤", "Remove Tag", "タグ削除" },
        ["menu.upload"]  = new[] { "上传云端", "上傳雲端", "Upload", "アップロード" },
        ["menu.delete"]  = new[] { "删除", "刪除", "Delete", "削除" },

        ["about.name"]      = new[] { "光遇-Musa 自动弹琴", "光遇-Musa 自動彈琴", "Sky-Musa Auto Play", "Sky-Musa 自動演奏" },
        ["about.version"]   = new[] { "软件版本", "軟體版本", "Version", "バージョン" },
        ["about.author"]    = new[] { "软件作者", "軟體作者", "Author", "作者" },
        ["about.repo"]      = new[] { "软件仓库", "軟體倉庫", "Repository", "リポジトリ" },
        ["about.check"]     = new[] { "检查更新", "檢查更新", "Check Update", "更新を確認" },
        ["about.lastcheck"] = new[] { "最后一次检查", "最後檢查", "Last check", "最終確認" },
        ["about.never"]     = new[] { "从未", "從未", "Never", "なし" },
        ["about.lang"]      = new[] { "语言", "語言", "Language", "言語" },
        ["about.log"]       = new[] { "上传日志", "上傳日誌", "Upload Log", "ログ送信" },
        ["about.latest"]    = new[] { "已是最新版本。", "已是最新版本。", "You have the latest version.", "最新バージョンです。" },

        // ── 侧边栏 ──
        ["nav.local"]    = new[] { "本地曲库", "本地曲庫", "Local", "ローカル" },
        ["nav.cloud"]    = new[] { "云端曲库", "雲端曲庫", "Cloud", "クラウド" },
        ["side.folders"] = new[] { "我的收藏夹", "我的收藏夾", "My Collections", "コレクション" },
        ["side.import"]  = new[] { "导入", "匯入", "Import", "インポート" },
        ["side.settings"]= new[] { "设置", "設定", "Settings", "設定" },
        ["profile.guest"]= new[] { "未登录", "未登入", "Not signed in", "未ログイン" },
        ["profile.login"]= new[] { "点此登录", "點此登入", "Tap to sign in", "タップしてログイン" },
        ["profile.in"]   = new[] { "已登录", "已登入", "Signed in", "ログイン済み" },
        ["profile.acct"] = new[] { "缪斯树屋账号", "繆斯樹屋帳號", "MuseTreehouse account", "MuseTreehouse アカウント" },
        ["unit.songs"]   = new[] { "首", "首", "songs", "曲" },

        // ── 设置界面 ──
        ["set.title"]    = new[] { "设置", "設定", "Settings", "設定" },
        ["set.lang"]     = new[] { "语言:", "語言:", "Language:", "言語:" },
        ["set.theme"]    = new[] { "主题:", "主題:", "Theme:", "テーマ:" },
        ["set.update"]   = new[] { "更新:", "更新:", "Update:", "更新:" },
        ["set.log"]      = new[] { "日志:", "日誌:", "Logs:", "ログ:" },
        ["set.ui"]       = new[] { "界面:", "介面:", "UI:", "UI:" },
        ["set.font"]     = new[] { "字体:", "字體:", "Font:", "フォント:" },
        ["set.wait"]     = new[] { "等待:", "等待:", "Wait:", "待機:" },
        ["set.bind"]     = new[] { "绑定:", "綁定:", "Keys:", "割当:" },
        ["set.checkupd"] = new[] { "检查更新", "檢查更新", "Check for updates", "更新を確認" },
        ["set.uplog"]    = new[] { "上传日志", "上傳日誌", "Upload logs", "ログ送信" },
        ["set.bindEdit"] = new[] { "点击修改", "點擊修改", "Click to edit", "クリックで編集" },
        ["set.bindDone"] = new[] { "完成绑定", "完成綁定", "Done", "完了" },
        ["set.bindHint"] = new[] { "点上方琴键格 → 按物理键完成重映射", "點上方琴鍵格 → 按物理鍵完成重映射", "Click a key above, then press a physical key to remap", "上のキーをクリックし、物理キーを押して再割当" },
        ["set.softinfo"] = new[] { "软件信息", "軟體資訊", "About", "ソフト情報" },

        // ── 中栏 ──
        ["cloud.newest"] = new[] { "最新", "最新", "Newest", "最新" },
        ["cloud.hot"]    = new[] { "最热", "最熱", "Hottest", "人気" },
        ["cloud.downs"]  = new[] { "下载量", "下載量", "Downloads", "DL数" },
        ["cloud.diffall"]= new[] { "全部难度", "全部難度", "All difficulty", "全難易度" },
        ["search.hint"]  = new[] { "搜索曲名", "搜尋曲名", "Search songs", "曲名を検索" },
        ["song.noartist"]= new[] { "未知作者", "未知作者", "Unknown artist", "作者不明" },
        ["song.notrans"] = new[] { "未知创谱者", "未知創譜者", "Unknown transcriber", "採譜者不明" },

        // ── 右栏 ──
        ["right.create"] = new[] { "创建", "創建", "Create", "新規" },
        ["right.practice"]=new[] { "练习", "練習", "Practice", "練習" },
        ["practice.back"] = new[] { "‹ 返回", "‹ 返回", "‹ Back", "‹ 戻る" },

        // ── 底部播放器 ──
        ["player.nosong"]= new[] { "未有正在播放的歌曲", "沒有正在播放的歌曲", "Nothing playing", "再生中の曲なし" },
        ["player.artist"]= new[] { "作者", "作者", "Artist", "作者" },
        ["player.trans"] = new[] { "创谱者", "創譜者", "Transcriber", "採譜者" },
        ["tip.playlist"] = new[] { "播放列表", "播放清單", "Play queue", "再生キュー" },
        ["tip.preview"]  = new[] { "试听模式(走扬声器不发按键)", "試聽模式(走喇叭不發按鍵)", "Preview mode (speaker, no key press)", "プレビュー(スピーカー、キー送信なし)" },
        ["tip.cave"]     = new[] { "洞穴音效开关", "洞穴音效開關", "Cave reverb toggle", "洞窟エコー切替" },
        ["tip.inst"]     = new[] { "选择音色", "選擇音色", "Choose instrument", "音色を選択" },
        ["tip.playmode"] = new[] { "播放方式", "播放方式", "Play mode", "再生モード" },
        ["tip.prev"]     = new[] { "上一首", "上一首", "Previous", "前へ" },
        ["tip.next"]     = new[] { "下一首", "下一首", "Next", "次へ" },
        ["tip.seek"]     = new[] { "拖动跳转播放位置", "拖動跳轉播放位置", "Drag to seek", "ドラッグでシーク" },

        // ── 播放列表面板 ──
        ["pl.title"]     = new[] { "播放列表", "播放清單", "Play Queue", "再生キュー" },
        ["pl.clear"]     = new[] { "清空", "清空", "Clear", "クリア" },
        ["pl.empty"]     = new[] { "双击左侧曲库里的歌曲, 即可加入播放列表", "雙擊左側曲庫裡的歌曲, 即可加入播放清單", "Double-click a song in the library to add it here", "左のライブラリの曲をダブルクリックで追加" },

        // ── 右键 / 更多菜单 ──
        ["m.play"]       = new[] { "播放", "播放", "Play", "再生" },
        ["m.addQueue"]   = new[] { "添加到播放列表", "加入播放清單", "Add to queue", "キューに追加" },
        ["m.favTo"]      = new[] { "收藏到……", "收藏到……", "Add to collection…", "コレクションに追加…" },
        ["m.newFolder"]  = new[] { "新建收藏夹…", "新增收藏夾…", "New collection…", "新規コレクション…" },
        ["m.removeLib"]  = new[] { "从曲库中移除", "從曲庫中移除", "Remove from library", "ライブラリから削除" },
        ["m.removeQueue"]= new[] { "从列表中移除", "從清單中移除", "Remove from queue", "キューから削除" },
        ["m.openLoc"]    = new[] { "打开文件位置", "開啟檔案位置", "Open file location", "ファイルの場所を開く" },
        ["m.editSong"]   = new[] { "编辑曲目", "編輯曲目", "Edit sheet", "譜面を編集" },
        ["m.songInfo"]   = new[] { "歌曲信息", "歌曲資訊", "Song info", "曲情報" },
        ["m.uploadCloud"]= new[] { "上传云端", "上傳雲端", "Upload to cloud", "クラウドへ" },
        ["m.view"]       = new[] { "查看", "查看", "Open", "開く" },
        ["m.rename"]     = new[] { "重命名", "重新命名", "Rename", "名前変更" },
        ["m.delFolder"]  = new[] { "删除收藏夹", "刪除收藏夾", "Delete collection", "コレクション削除" },
        ["m.removeFromFolder"] = new[] { "从「{0}」移除", "從「{0}」移除", "Remove from \"{0}\"", "「{0}」から削除" },

        // ── 对话框 / 输入 ──
        ["d.newFolder"]  = new[] { "新建收藏夹", "新增收藏夾", "New collection", "新規コレクション" },
        ["d.folderName"] = new[] { "收藏夹名称:", "收藏夾名稱:", "Collection name:", "コレクション名:" },
        ["d.renameFolder"]=new[] { "重命名收藏夹", "重新命名收藏夾", "Rename collection", "コレクションの名前変更" },
        ["d.newName"]    = new[] { "新名称:", "新名稱:", "New name:", "新しい名前:" },
        ["d.ok"]         = new[] { "确定", "確定", "OK", "OK" },
        ["d.cancel"]     = new[] { "取消", "取消", "Cancel", "キャンセル" },

        // ── 确认 / 提示(toast) ──
        ["c.clearQueue"] = new[] { "确定清空播放列表?", "確定清空播放清單?", "Clear the play queue?", "再生キューをクリアしますか？" },
        ["c.queueTitle"] = new[] { "播放列表", "播放清單", "Play Queue", "再生キュー" },
        ["c.delFolder"]  = new[] { "确定删除收藏夹「{0}」?\n(只删歌单, 不删曲谱文件)", "確定刪除收藏夾「{0}」?\n(只刪清單, 不刪曲譜檔案)", "Delete collection \"{0}\"?\n(Removes the list only, not the sheet files)", "コレクション「{0}」を削除しますか？\n(リストのみ、譜面ファイルは残ります)" },
        ["c.removeLibConfirm"] = new[] { "确定从曲库移除「{0}」?\n将从磁盘永久删除, 无法撤销。", "確定從曲庫移除「{0}」?\n將從磁碟永久刪除, 無法復原。", "Remove \"{0}\" from the library?\nThis permanently deletes the file.", "「{0}」をライブラリから削除しますか？\nファイルは完全に削除されます。" },
        ["t.addedQueue"] = new[] { "已加入播放列表「{0}」", "已加入播放清單「{0}」", "Added \"{0}\" to queue", "「{0}」をキューに追加" },
        ["t.inQueue"]    = new[] { "「{0}」已在播放列表", "「{0}」已在播放清單", "\"{0}\" is already in the queue", "「{0}」は既にキューにあります" },
        ["t.emptyQueue"] = new[] { "播放列表为空, 双击曲库里的歌曲加入", "播放清單為空, 雙擊曲庫裡的歌曲加入", "Queue is empty — double-click a song to add", "キューが空です — 曲をダブルクリックで追加" },
        ["t.favTo"]      = new[] { "已收藏到「{0}」", "已收藏到「{0}」", "Added to \"{0}\"", "「{0}」に追加" },
        ["t.removedFrom"]= new[] { "已从「{0}」移除", "已從「{0}」移除", "Removed from \"{0}\"", "「{0}」から削除" },
        ["t.unfav"]      = new[] { "已取消收藏", "已取消收藏", "Removed from favorites", "お気に入り解除" },
        ["t.removedLib"] = new[] { "已移除「{0}」", "已移除「{0}」", "Removed \"{0}\"", "「{0}」を削除" },
        ["t.newFolder"]  = new[] { "已新建收藏夹「{0}」", "已新增收藏夾「{0}」", "Created collection \"{0}\"", "コレクション「{0}」を作成" },
        ["t.uploaded"]   = new[] { "已上传「{0}」", "已上傳「{0}」", "Uploaded \"{0}\"", "「{0}」をアップロード" },
        ["t.downloading"]= new[] { "下载中「{0}」…", "下載中「{0}」…", "Downloading \"{0}\"…", "「{0}」をダウンロード中…" },
        ["t.downloaded"] = new[] { "已下载「{0}」到本地曲库", "已下載「{0}」到本地曲庫", "Downloaded \"{0}\" to the library", "「{0}」をライブラリに保存" },
        ["t.cloudEmpty"] = new[] { "没有匹配的曲谱", "沒有匹配的曲譜", "No matching sheets", "一致する譜面なし" },
        ["t.instTo"]     = new[] { "音色 → {0}", "音色 → {0}", "Instrument → {0}", "音色 → {0}" },
        ["t.bindOn"]     = new[] { "点上方琴键格, 再按物理键重映射", "點上方琴鍵格, 再按物理鍵重映射", "Click a key above, then press a physical key", "上のキーをクリック後、物理キーを押す" },
        ["t.bindOff"]    = new[] { "按键映射已保存", "按鍵映射已儲存", "Key mapping saved", "キー割当を保存しました" },
        ["t.uploading"]  = new[] { "上传日志中…", "上傳日誌中…", "Uploading logs…", "ログ送信中…" },
        ["t.logok"]      = new[] { "日志已上传, 感谢反馈", "日誌已上傳, 感謝回饋", "Logs uploaded — thanks!", "ログを送信しました。ありがとう！" },
        ["t.logfail"]    = new[] { "上传失败: ", "上傳失敗: ", "Upload failed: ", "送信失敗: " },
        ["t.latest"]     = new[] { "已是最新版本", "已是最新版本", "You're on the latest version", "最新バージョンです" },
        ["t.practice"]   = new[] { "跟弹练习中(不发游戏按键)", "跟彈練習中(不發遊戲按鍵)", "Practice mode (no game keys)", "練習モード(ゲームキー送信なし)" },
        ["t.practiceDone"]=new[] { "本曲跟弹完成! 从头再来", "本曲跟彈完成! 從頭再來", "Song complete! Restarting", "曲を弾き終えました! 最初から" },
    };

    // 音色名(内部键为英文, 显示按语言翻译)
    static readonly Dictionary<string, string[]> Inst = new()
    {
        ["Piano"]           = new[] { "钢琴", "鋼琴", "Piano", "ピアノ" },
        ["Harp"]            = new[] { "竖琴", "豎琴", "Harp", "ハープ" },
        ["Guitar"]          = new[] { "吉他", "吉他", "Guitar", "ギター" },
        ["Flute"]           = new[] { "长笛", "長笛", "Flute", "フルート" },
        ["Ukulele"]         = new[] { "尤克里里", "烏克麗麗", "Ukulele", "ウクレレ" },
        ["Winter Piano"]    = new[] { "冬季钢琴", "冬季鋼琴", "Winter Piano", "ウィンターピアノ" },
        ["Xylophone"]       = new[] { "木琴", "木琴", "Xylophone", "シロフォン" },
        ["Electric Guitar"] = new[] { "电吉他", "電吉他", "Electric Guitar", "エレキギター" },
        ["Bassoon"]         = new[] { "巴松管", "巴松管", "Bassoon", "ファゴット" },
        ["Orff"]            = new[] { "奥尔夫", "奧爾夫", "Orff", "オルフ" },
        ["Kalimba"]         = new[] { "卡林巴", "卡林巴", "Kalimba", "カリンバ" },
        ["Ocarina"]         = new[] { "陶笛", "陶笛", "Ocarina", "オカリナ" },
        ["Cello"]           = new[] { "大提琴", "大提琴", "Cello", "チェロ" },
        ["Violin"]          = new[] { "小提琴", "小提琴", "Violin", "ヴァイオリン" },
        ["Saxophone"]       = new[] { "萨克斯", "薩克斯", "Saxophone", "サックス" },
        ["Pipa"]            = new[] { "琵琶", "琵琶", "Pipa", "ピパ" },
        ["Quena"]           = new[] { "盖那笛", "蓋那笛", "Quena", "ケーナ" },
        ["Bugle"]           = new[] { "军号", "軍號", "Bugle", "ビューグル" },
        ["Glock"]           = new[] { "钟琴", "鐘琴", "Glockenspiel", "グロッケン" },
        ["LightGuitar"]     = new[] { "轻吉他", "輕吉他", "Light Guitar", "ライトギター" },
        ["GoldPiano"]       = new[] { "金钢琴", "金鋼琴", "Gold Piano", "ゴールドピアノ" },
        ["Horn"]            = new[] { "圆号", "圓號", "Horn", "ホルン" },
        ["Handpan"]         = new[] { "手碟", "手碟", "Handpan", "ハンドパン" },
        ["GoldHandpan"]     = new[] { "金手碟", "金手碟", "Gold Handpan", "ゴールドハンドパン" },
        ["Dundun"]          = new[] { "邓杜鼓", "鄧杜鼓", "Dundun", "ドゥンドゥン" },
        ["APBell1"]         = new[] { "铃1", "鈴1", "AP Bell 1", "ベル1" },
        ["APBell2"]         = new[] { "铃2", "鈴2", "AP Bell 2", "ベル2" },
        ["Harmonica"]       = new[] { "口琴", "口琴", "Harmonica", "ハーモニカ" },
        ["AP18Ocarina"]     = new[] { "陶笛Ⅱ", "陶笛Ⅱ", "Ocarina II", "オカリナⅡ" },
        ["AP29Piccolo"]     = new[] { "短笛", "短笛", "Piccolo", "ピッコロ" },
        ["GoldBugle"]       = new[] { "金军号", "金軍號", "Gold Bugle", "ゴールドビューグル" },
        ["APPiano"]         = new[] { "AP钢琴", "AP鋼琴", "AP Piano", "APピアノ" },
        ["4thAnnivArp"]     = new[] { "四周年·琶音", "四週年·琶音", "4th Anniv Arp", "4周年アルペジオ" },
        ["4thAnnivLead"]    = new[] { "四周年·主音", "四週年·主音", "4th Anniv Lead", "4周年リード" },
        ["Contrabass"]      = new[] { "低音提琴", "低音提琴", "Contrabass", "コントラバス" },
        ["4thAnnivBass"]    = new[] { "四周年·贝斯", "四週年·貝斯", "4th Anniv Bass", "4周年ベース" },
        ["GoldDundun"]      = new[] { "金邓杜鼓", "金鄧杜鼓", "Gold Dundun", "ゴールドドゥンドゥン" },
    };

    public static string S(string key) => T.TryGetValue(key, out var a) ? a[(int)Current] : key;
    public static string Instrument(string key) => Inst.TryGetValue(key, out var a) ? a[(int)Current] : key;

    public static void Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && Enum.TryParse<AppLang>(System.IO.File.ReadAllText(File).Trim(), out var l))
                Current = l;
        }
        catch { }
    }

    public static void Set(AppLang l)
    {
        Current = l;
        try { Directory.CreateDirectory(Path.GetDirectoryName(File)!); System.IO.File.WriteAllText(File, l.ToString()); } catch { }
    }
}
