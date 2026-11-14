namespace SMAP_WPF;

public static partial class Lang
{
    static void AddMappingTranslations()
    {
        void Add(string key,string zh,string tw,string en,string ja)=>T["map."+key]=new[]{zh,tw,en,ja};
        Add("title","游戏谱面映射","遊戲譜面映射","Game sheet mapping","ゲーム譜面マッピング");
        Add("deferredTitle","游戏谱面映射 · v3.1 暂不可用","遊戲譜面映射 · v3.1 暫不可用","Game sheet mapping · v3.1 unavailable","ゲーム譜面マッピング · v3.1 利用不可");
        Add("deferred","v3.0 暂不可用，计划于 v3.1 提供；原有配置保留。","v3.0 暫不可用，計劃於 v3.1 提供；原有設定保留。","Unavailable in v3.0; planned for v3.1. Existing settings are retained.","v3.0 では利用できません。v3.1 で提供予定です。既存の設定は保持されます。");
        Add("window","游戏窗口","遊戲視窗","Game window","ゲームウィンドウ");
        Add("noGame","未找到可用的光遇窗口。请先打开游戏乐器，再刷新。","未找到可用的光遇視窗。請先開啟遊戲樂器，再重新整理。","No available Sky window. Open the game instrument, then refresh.","利用可能な Sky ウィンドウがありません。楽器を開いて更新してください。");
        Add("shortcuts","功能快捷键","功能快捷鍵","Function shortcuts","機能ショートカット");
        Add("shortcutHint","点击组合键直接录入，Esc 取消。Windows 键组合及系统保留键不可用。","點擊組合鍵直接錄入，Esc 取消。Windows 鍵組合及系統保留鍵無法使用。","Click a binding to record; Esc cancels. Windows key and system reserved combinations are unavailable.","割当をクリックして入力、Esc で取消。Windows キーとシステム予約済みの組合せは利用できません。");
        Add("bindings","演奏键位","演奏鍵位","Performance bindings","演奏キー設定");
        Add("unbound","未绑定","未綁定","Unbound","未割当");
        Add("stopKey","⏹ 停止 (F1)","⏹ 停止 (F1)","⏹ Stop (F1)","⏹ 停止 (F1)");
        Add("resumeKey","▶ 继续 (F2)","▶ 繼續 (F2)","▶ Resume (F2)","▶ 再開 (F2)");
        Add("playingKeys","状态: 🎵 演奏中… (F1 停止 / F2 暂停)","狀態: 🎵 演奏中… (F1 停止 / F2 暫停)","Status: 🎵 Playing… (F1 stop / F2 pause)","状態: 🎵 演奏中… (F1 停止 / F2 一時停止)");
        Add("capture","请按组合键… Esc 取消","請按組合鍵… Esc 取消","Press a chord… Esc cancels","キーを押す… Esc で取消");
        Add("global","全局","全域","Global","グローバル"); Add("game","游戏前台","遊戲前景","Game foreground","ゲーム前面"); Add("app","SMAP 前台","SMAP 前景","SMAP foreground","SMAP 前面");
        Add("scope","作用范围","作用範圍","Scope","適用範囲");
        Add("defaults","恢复默认","恢復預設","Restore defaults","初期設定に戻す");
        Add("save","检查并保存","檢查並儲存","Validate and save","確認して保存");
        Add("available","已验证可用","已驗證可用","Verified available","利用可能を確認済み");
        Add("unavailable","已验证不可用","已驗證不可用","Verified unavailable","利用不可を確認済み");
        Add("unknown","未验证 / 未知","未驗證 / 未知","Unverified / unknown","未検証 / 不明");
        Add("detect","自动检测","自動偵測","Auto detect","自動検出"); Add("manual","手动校准","手動校準","Manual calibration","手動調整");
        Add("follow","跟弹推进","跟彈推進","Follow input","入力に追従"); Add("rhythm","节奏推进","節奏推進","Follow rhythm","リズムに追従");
        Add("start","开始 / 继续","開始 / 繼續","Start / resume","開始 / 再開"); Add("pause","暂停","暫停","Pause","一時停止");
        Add("exit","退出映射","退出映射","Exit mapping","マッピング終了");
        Add("confirm","确认位置并保存","確認位置並儲存","Confirm and save positions","位置を確認して保存");
        Add("review","请确认标记与游戏琴键中心一致","請確認標記與遊戲琴鍵中心一致","Confirm markers match the game key centers","マーカーがゲームキーの中心にあることを確認");
        Add("failed","检测不确定，请手动校准","偵測不確定，請手動校準","Detection uncertain. Calibrate manually","検出が不確実です。手動で調整してください");
        Add("inactive","游戏窗口已最小化、关闭或失效，请恢复游戏后刷新","遊戲視窗已最小化、關閉或失效，請恢復遊戲後重新整理","Game window minimized, closed or unavailable. Restore it and refresh.","ゲーム画面が最小化・終了または利用不可です。復元して更新してください。");
        Add("focus","游戏仍在，请切回游戏显示提示。操作 SMAP 时无需保持游戏焦点。","遊戲仍在，請切回遊戲顯示提示。操作 SMAP 時無需保持遊戲焦點。","Game window is available. Return to it for hints; using SMAP normally changes focus.","ゲームは開いています。ヒントを見るにはゲームに戻ってください。SMAP の操作中はフォーカスが移ります。");
        Add("covered","游戏窗口被其他窗口遮挡，提示已暂停，请移开遮挡后继续","遊戲視窗被其他視窗遮擋，提示已暫停，請移開遮擋後繼續","Another window covers the game. Hints paused; move it away to continue.","別の画面がゲームを覆っているため一時停止中です。覆っている画面を移動してください。");
        Add("changed","窗口尺寸或 DPI 已变化，请重新校准","視窗尺寸或 DPI 已變更，請重新校準","Window size or DPI changed. Recalibrate","ウィンドウサイズまたは DPI が変更。再調整が必要です");
        Add("uncalibrated","尚未设置琴键位置，请自动检测或手动校准","尚未設定琴鍵位置，請自動偵測或手動校準","Key positions are not set. Auto detect or calibrate manually.","キーの位置が未設定です。自動検出または手動調整を行ってください。");
        Add("readonly","仅提示，不发送游戏演奏按键","僅提示，不傳送遊戲演奏按鍵","Hints only. No automatic game input","ヒントのみ。ゲームへの自動入力なし");
        Add("unknownConfirm","能力资料尚未完成游戏验证。请确认实际乐器、键位和绑定一致后继续。","能力資料尚未完成遊戲驗證。請確認實際樂器、鍵位和綁定一致後繼續。","Capabilities are unverified in game. Confirm instrument, keys and bindings before continuing.","ゲーム内の能力は未検証です。楽器・キー・割当を確認して続行してください。");
        Add("unsupported","曲谱存在不可演奏或未配置的琴键，已阻止游戏演奏。","曲譜存在無法演奏或未配置的琴鍵，已阻止遊戲演奏。","The score has unavailable or unconfigured keys. Game playback blocked.","演奏不可または未設定のキーがあるため、ゲーム演奏を停止しました。");
        Add("ready","位置已确认 · 按开始继续","位置已確認 · 按開始繼續","Positions confirmed. Start to continue","位置を確認済み。開始で続行");
        Add("armed","练习已准备，请切回游戏窗口","練習已準備，請切回遊戲視窗","Practice ready. Return to the game window.","練習の準備完了。ゲーム画面に戻ってください。");
        Add("running","练习进行中 · 仅提示，不自动演奏","練習進行中 · 僅提示，不自動演奏","Practice running. Hints only; no automatic input.","練習中。ヒントのみで自動演奏は行いません。");
        Add("paused","练习已暂停，按开始继续","練習已暫停，按開始繼續","Practice paused. Start to resume.","練習を一時停止中。開始で再開してください。");
        Add("retained","保留上次确认的位置，开始前将重新复核","保留上次確認的位置，開始前將重新複核","Previous confirmed positions retained. They will be checked before starting.","前回確認した位置を保持しました。開始前に再確認します。");
        Add("hold","保持","保持","Hold","保持");Add("release","松开 ↑","鬆開 ↑","Release ↑","離す ↑");Add("next","下一步","下一步","Next","次");Add("now","现在按","現在按","Press","押す");
        Add("error","错误 ×","錯誤 ×","Error ×","エラー ×");Add("done","练习完成 ✓","練習完成 ✓","Practice complete ✓","練習完了 ✓");
        Add("custom4","自定义四键 · 未验证","自訂四鍵 · 未驗證","Custom four keys · unverified","カスタム4キー・未検証");
        Add("click","请依次拖框圈住每个真实琴键","請依次拖框圈住每個真實琴鍵","Drag a rectangle around each real key in order","実際の各キーを順にドラッグで囲んでください");
        Add("clear","清除","清除","Clear","解除");
        Add("percussion","打击参考","打擊參考","Percussion","打楽器参照");
        string[] zh={"播放 / 停止","暂停 / 继续","上一首","下一首","减速","加速","快退","快进","试听 / 演奏模式","开始 / 退出练习","显示 / 隐藏映射","暂停 / 恢复映射","显示 / 隐藏主窗口"};
        string[] tw={"播放 / 停止","暫停 / 繼續","上一首","下一首","減速","加速","快退","快進","試聽 / 演奏模式","開始 / 退出練習","顯示 / 隱藏映射","暫停 / 恢復映射","顯示 / 隱藏主視窗"};
        string[] en={"Play / stop","Pause / resume","Previous","Next","Slower","Faster","Seek back","Seek forward","Preview / game mode","Enter / exit practice","Show / hide mapping","Pause / resume mapping","Show / hide main window"};
        string[] ja={"再生 / 停止","一時停止 / 再開","前の曲","次の曲","減速","加速","巻き戻し","早送り","試聴 / 演奏切替","練習開始 / 終了","マッピング表示 / 非表示","マッピング一時停止 / 再開","メイン表示 / 非表示"};
        for(int i=0;i<zh.Length;i++)Add("action."+i,zh[i],tw[i],en[i],ja[i]);
        Add("DuplicateShortcut","作用范围重叠，快捷键重复。","作用範圍重疊，快捷鍵重複。","Duplicate shortcut in overlapping scopes.","重複する適用範囲でショートカットが重複しています。");
        Add("PerformanceKeyConflict","与演奏键冲突。","與演奏鍵衝突。","Conflicts with a performance key.","演奏キーと競合しています。");
        Add("ReservedShortcut","此组合为系统保留或不能安全注册。","此組合為系統保留或不能安全註冊。","System reserved or unsafe combination.","システム予約済みまたは安全に登録できないキーです。");
        Add("applyFailed","应用失败，原配置继续保留；请查看错误详情。","套用失敗，原配置繼續保留；請查看錯誤詳情。","Apply failed. Previous configuration retained; check error details.","適用に失敗。元の設定を保持しました。詳細を確認してください。");
        Add("rollbackFailed","快捷键恢复未完成，部分绑定可能暂不可用。请查看详情并重新应用配置。","快捷鍵恢復未完成，部分綁定可能暫時無法使用。請查看詳情並重新套用設定。","Shortcut recovery failed. Some bindings may be unavailable. Check details and reapply.","ショートカットの復元に失敗。一部の割当が利用できない可能性があります。詳細を確認し再適用してください。");
    }
}
