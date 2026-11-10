namespace SMAP_WPF;

public static partial class Lang
{
    static readonly Dictionary<string, string[]> LoginText = new()
    {
        ["title"] = ["账号登录", "帳號登入", "Sign in", "アカウントログイン"],
        ["brand"] = ["缪斯树屋", "繆斯樹屋", "Muse Treehouse", "Muse Treehouse"],
        ["username"] = ["用户名或邮箱", "使用者名稱或電子郵件", "Username or email", "ユーザー名またはメールアドレス"],
        ["password"] = ["密码", "密碼", "Password", "パスワード"],
        ["cancel"] = ["取消", "取消", "Cancel", "キャンセル"],
        ["submit"] = ["登录", "登入", "Sign in", "ログイン"],
        ["required"] = ["请输入账号和密码", "請輸入帳號及密碼", "Enter your username and password.", "ユーザー名とパスワードを入力してください。"],
        ["working"] = ["登录中…", "登入中…", "Signing in…", "ログイン中…"]
    };

    public static string Login(string key) => LoginText.TryGetValue(key, out var values) ? values[(int)Current] : key;
}
