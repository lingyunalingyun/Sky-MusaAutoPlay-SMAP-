using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>个人信息窗口: 头像 + 用户名 + 个人主页 / 退出账号。</summary>
public class ProfileWindow : ChromeWindow
{
    public bool LoggedOut { get; private set; }

    public ProfileWindow(Window owner) : base("个人信息", 640)
    {
        Owner = owner;

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };

        // 头像 + 用户名
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 24) };
        var avatar = new Grid { Width = 100, Height = 100 };
        avatar.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)), Stroke = B("BtnBorder"), StrokeThickness = 1 });
        var avatarPhoto = new System.Windows.Shapes.Ellipse { Visibility = Visibility.Collapsed };
        var avatarInitial = new TextBlock
        {
            Text = (CloudApi.Username ?? "?").Substring(0, 1).ToUpper(),
            FontSize = 40, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xb0, 0xb0, 0xc0)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Children.Add(avatarPhoto);
        avatar.Children.Add(avatarInitial);
        LoadAvatar(avatarPhoto, avatarInitial);
        top.Children.Add(avatar);
        top.Children.Add(new TextBlock
        {
            Text = CloudApi.Username ?? "用户名",
            FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = B("TextFg"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0)
        });
        body.Children.Add(top);

        // 个人主页 / 退出账号
        var btns = new Grid();
        btns.ColumnDefinitions.Add(new ColumnDefinition());
        btns.ColumnDefinitions.Add(new ColumnDefinition());
        var home = Neutral("个人主页"); home.Margin = new Thickness(0, 0, 12, 0);
        home.Click += (_, __) => Open("http://musetreehouse.com");
        var logout = Neutral("退出账号"); logout.Margin = new Thickness(12, 0, 0, 0);
        logout.Click += (_, __) => { CloudApi.Logout(); LoggedOut = true; Close(); };
        Grid.SetColumn(logout, 1);
        btns.Children.Add(home);
        btns.Children.Add(logout);
        body.Children.Add(btns);

        SetBody(body);
    }

    static async void LoadAvatar(System.Windows.Shapes.Ellipse photo, TextBlock initial)
    {
        var image = await AvatarUtil.LoadAsync();
        if (image == null) return;
        photo.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        photo.Visibility = Visibility.Visible;
        initial.Visibility = Visibility.Collapsed;
    }

    static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
