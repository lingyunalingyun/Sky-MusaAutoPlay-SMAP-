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
    };

    public static string S(string key) => T.TryGetValue(key, out var a) ? a[(int)Current] : key;

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
