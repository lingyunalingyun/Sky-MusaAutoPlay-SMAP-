using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>缪斯树屋登录对话框。成功后 CloudApi 写入登录态，DialogResult=true。</summary>
public class LoginDialog : ChromeWindow
{
    public LoginDialog(Window owner) : this(owner, CloudApi.LoginAsync) { }

    internal LoginDialog(Window owner, Func<string, string, Task<string?>> authenticate) : base(Lang.Login("title"), 420)
    {
        Owner = owner;
        var panel = new Grid { Height = 358, Margin = new Thickness(24, 0, 24, 0) };
        void Place(FrameworkElement element, double top)
        { element.VerticalAlignment = VerticalAlignment.Top; element.Margin = new Thickness(0, top, 0, 0); panel.Children.Add(element); }
        TextBlock Label(string text, double size, string brush = "SubTextFg")
        {
            var label = new TextBlock { Text = text, FontSize = size };
            label.SetResourceReference(TextBlock.ForegroundProperty, brush); return label;
        }

        var title = Label(Lang.Login("brand"), 20, "TextFg"); title.FontWeight = FontWeights.SemiBold;
        Place(title, 23); Place(Label("musetreehouse.com", 12), 56);
        Place(Label(Lang.Login("username"), 13), 89);
        Place(Label(Lang.Login("password"), 13), 169);

        var user = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        var pass = new PasswordBox { VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var field in new Control[] { user, pass })
        {
            field.Height = 42; field.FontSize = 14; field.BorderThickness = new Thickness(1);
            field.FocusVisualStyle = null; field.Template = InputTemplate(field is PasswordBox ? "PasswordBox" : "TextBox");
            field.SetResourceReference(BackgroundProperty, "BoxBg"); field.SetResourceReference(ForegroundProperty, "TextFg");
            field.SetResourceReference(BorderBrushProperty, "BoxBorder");
        }
        user.SetResourceReference(TextBox.CaretBrushProperty, "TextFg");
        pass.SetResourceReference(PasswordBox.CaretBrushProperty, "TextFg");
        Place(user, 113); Place(pass, 193);

        var error = Label("", 12);
        error.Foreground = (Brush)new BrushConverter().ConvertFromString(Theme.Current switch
        { AppTheme.Light => "#b43131", AppTheme.SunsetPink => "#a42957", _ => "#ff8c8c" })!;
        error.TextWrapping = TextWrapping.Wrap;
        Place(new ScrollViewer { Content = error, Height = 38, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 244);

        var cancel = Neutral(Lang.Login("cancel")); cancel.Height = 42; cancel.IsCancel = true;
        var login = Neutral(Lang.Login("submit")); login.Height = 42; login.IsDefault = true; login.FontWeight = FontWeights.SemiBold;
        login.SetResourceReference(BackgroundProperty, "PracticeActionBg"); login.SetResourceReference(ForegroundProperty, "ActionFg");
        login.SetResourceReference(BorderBrushProperty, "PracticeActionBg");
        var actions = new Grid { Height = 42 };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.Children.Add(cancel); Grid.SetColumn(login, 2); actions.Children.Add(login); Place(actions, 289);

        bool busy = false, closed = false;
        async void DoLogin()
        {
            if (busy || closed) return;
            string username = user.Text.Trim();
            if (username.Length == 0 || pass.Password.Length == 0) { error.Text = Lang.Login("required"); return; }
            busy = true; login.IsEnabled = user.IsEnabled = pass.IsEnabled = false;
            login.Content = Lang.Login("working"); error.Text = "";
            string? message = await authenticate(username, pass.Password);
            if (closed) return;
            if (message == null) { DialogResult = true; return; }
            error.Text = message; busy = false; login.IsEnabled = user.IsEnabled = pass.IsEnabled = true;
            login.Content = Lang.Login("submit");
        }
        login.Click += (_, _) => DoLogin();
        // IsDefault owns Enter; a second PasswordBox key handler would submit twice.
        Loaded += (_, _) => user.Focus(); Closed += (_, _) => closed = true;
        SetBody(panel);
    }

    static ControlTemplate InputTemplate(string target) => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate TargetType='" + target + "' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
        "<Border x:Name='field' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='12,0'>" +
        "<ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='field' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>" +
        "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='field' Property='Opacity' Value='0.65'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
}
