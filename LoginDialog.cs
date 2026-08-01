using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>缪斯树屋登录对话框。成功后 CloudApi 写入登录态, DialogResult=true。</summary>
public class LoginDialog : Window
{
    public LoginDialog(Window owner)
    {
        Title = "登录 — 缪斯树屋";
        Width = 340; Owner = owner;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));

        var title = new TextBlock { Text = "缪斯树屋", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)), HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock { Text = "musetreehouse.com", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 16) };

        var user = MakeText();
        var pass = PassInput();
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x6b, 0x6b)), FontSize = 11, TextWrapping = TextWrapping.Wrap, MinHeight = 18, Margin = new Thickness(0, 2, 0, 0) };

        var login = new Button { Content = "登  录", Height = 38, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x4d, 0x8e, 0xff)), IsDefault = true };
        var cancel = new Button { Content = "取消", Height = 32, IsCancel = true, Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)) };

        async void DoLogin()
        {
            var u = user.Text.Trim();
            if (u.Length == 0 || pass.Password.Length == 0) { error.Text = "请输入账号和密码"; return; }
            login.IsEnabled = false; login.Content = "登录中..."; error.Text = "";
            var err = await CloudApi.LoginAsync(u, pass.Password);
            if (err == null) { DialogResult = true; }
            else { error.Text = err; login.IsEnabled = true; login.Content = "登  录"; }
        }
        login.Click += (_, __) => DoLogin();
        pass.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) DoLogin(); };

        var panel = new StackPanel { Margin = new Thickness(32, 8, 32, 20) };
        panel.Children.Add(title);
        panel.Children.Add(sub);
        panel.Children.Add(Labeled("用户名或邮箱", user));
        panel.Children.Add(Labeled("密码", pass));
        panel.Children.Add(error);
        panel.Children.Add(login);
        panel.Children.Add(cancel);
        Content = panel;
        Loaded += (_, __) => user.Focus();
    }

    static TextBox MakeText() => new()
    {
        Height = 34, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13,
        Background = new SolidColorBrush(Color.FromRgb(0x4a, 0x4a, 0x4a)), Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
    };

    static PasswordBox PassInput() => new()
    {
        Height = 34, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13,
        Background = new SolidColorBrush(Color.FromRgb(0x4a, 0x4a, 0x4a)), Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
    };

    static StackPanel Labeled(string label, Control field)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        p.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa)), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) });
        p.Children.Add(field);
        return p;
    }
}
