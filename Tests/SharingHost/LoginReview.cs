using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;

static partial class Program
{
    static LoginDialog LoginWindow(Window owner, Func<string, string, Task<string?>> authenticate)
        => (LoginDialog)typeof(LoginDialog).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(Window), typeof(Func<string, string, Task<string?>>)], null)!.Invoke([owner, authenticate]);

    static void LoginReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var owner = new Window { Width = 200, Height = 100, ShowInTaskbar = false, Left = -12000, Top = -12000 }; owner.Show();
        var output = Path.Combine(Output, "gui/login-review/actual"); Directory.CreateDirectory(output);
        void Capture(Window window, string name)
        {
            Pump();window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png"));encoder.Save(file);
        }
        foreach (AppTheme theme in Enum.GetValues<AppTheme>()) foreach (AppLang language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language);
            int requests = 0;var completion = new TaskCompletionSource<string?>();
            var window = LoginWindow(owner, (username, password) =>
            { requests++;Check("LOGIN-CREDENTIAL-VALUES-"+theme+language,username=="synthetic@example.invalid"&&password=="synthetic-only");return completion.Task; });
            window.WindowStartupLocation = WindowStartupLocation.Manual;window.Left = -12000;window.Top = -12000;window.ShowInTaskbar = false;
            window.Show();Pump();window.UpdateLayout();
            var user = Descendants<TextBox>(window).Single();var pass = Descendants<PasswordBox>(window).Single();
            var submit = Descendants<Button>(window).Single(b => b.IsDefault);var cancel = Descendants<Button>(window).Single(b => b.IsCancel);
            string id = theme+"-"+language;
            Check("LOGIN-APPROVED-BOUNDS-"+id,window.ActualWidth==420&&window.ActualHeight==422&&user.ActualWidth==342&&pass.ActualWidth==342);
            Check("LOGIN-PARALLEL-ACTIONS-"+id,submit.TranslatePoint(new Point(),window).Y==cancel.TranslatePoint(new Point(),window).Y&&submit.ActualWidth==cancel.ActualWidth&&submit.ActualHeight==42);
            foreach (var field in new Control[]{user,pass})
            {
                var border = (Border)field.Template.FindName("field",field);
                Check("LOGIN-ROUND-FIELD-"+id+field.GetType().Name,border.CornerRadius==new CornerRadius(8)&&field.FontSize==14);
            }
            Check("LOGIN-LOCALIZED-"+id,window.Title==Lang.Login("title")&&submit.Content as string==Lang.Login("submit")&&cancel.Content as string==Lang.Login("cancel"));
            Capture(window,id+"-default");
            double height = window.ActualHeight;Point actions = submit.TranslatePoint(new Point(),window);
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check("LOGIN-EMPTY-NO-NETWORK-"+id,requests==0&&Descendants<TextBlock>(window).Any(t=>t.Text==Lang.Login("required")));
            user.Text=" synthetic@example.invalid ";pass.Password="synthetic-only";
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check("LOGIN-ONE-INFLIGHT-"+id,requests==1&&!submit.IsEnabled&&!user.IsEnabled&&!pass.IsEnabled&&submit.Content as string==Lang.Login("working"));
            Capture(window,id+"-loading");completion.SetResult(Lang.S("cloud.loginFailed"));
            var until=DateTime.UtcNow.AddSeconds(5);while(!submit.IsEnabled&&DateTime.UtcNow<until)Pump();
            Check("LOGIN-FAILED-RETRY-AND-STABLE-LAYOUT-"+id,submit.IsEnabled&&user.IsEnabled&&pass.IsEnabled&&window.ActualHeight==height&&submit.TranslatePoint(new Point(),window)==actions&&Descendants<TextBlock>(window).Any(t=>t.Text==Lang.S("cloud.loginFailed")));
            Check("LOGIN-PASSWORD-NOT-VISIBLE-"+id,!Descendants<TextBlock>(window).Any(t=>t.Text.Contains("synthetic-only")));
            Capture(window,id+"-error");window.Close();Pump();
        }
        Palette(AppTheme.Dark);Language(AppLang.ZhCN);
        int enterRequests=0;
        var enter=LoginWindow(owner,(_,_)=>{enterRequests++;return Task.FromResult<string?>(null);});
        enter.WindowStartupLocation=WindowStartupLocation.Manual;enter.Left=-12000;enter.Top=-12000;enter.ShowInTaskbar=false;
        enter.Loaded+=(_,_)=>enter.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            Descendants<TextBox>(enter).Single().Text="synthetic";var password=Descendants<PasswordBox>(enter).Single();password.Password="synthetic-only";password.Focus();
            AccessKeyManager.ProcessKey(PresentationSource.FromVisual(enter),"\r",false);
        }));
        var watchdog=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};watchdog.Tick+=(_,_)=>enter.Close();watchdog.Start();
        bool? enterResult=enter.ShowDialog();watchdog.Stop();Check("LOGIN-ENTER-SINGLE-SUCCESS",enterResult==true&&enterRequests==1);
        var escape=LoginWindow(owner,(_,_)=>throw new InvalidOperationException("Escape must not authenticate"));
        escape.WindowStartupLocation=WindowStartupLocation.Manual;escape.Left=-12000;escape.Top=-12000;escape.ShowInTaskbar=false;
        escape.Loaded+=(_,_)=>escape.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>AccessKeyManager.ProcessKey(PresentationSource.FromVisual(escape),"\x1b",false)));
        bool timedOut=false;var escapeDeadline=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};escapeDeadline.Tick+=(_,_)=>{timedOut=true;escape.Close();};escapeDeadline.Start();
        bool? escapeResult=escape.ShowDialog();escapeDeadline.Stop();Check("LOGIN-ESC-CANCEL",escapeResult==false&&!timedOut);
        var late=new TaskCompletionSource<string?>();var closed=LoginWindow(owner,(_,_)=>late.Task);closed.Show();Pump();
        Descendants<TextBox>(closed).Single().Text="synthetic";Descendants<PasswordBox>(closed).Single().Password="synthetic-only";
        Descendants<Button>(closed).Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));closed.Close();late.SetResult(null);Pump();
        Check("LOGIN-CLOSE-DURING-REQUEST-SAFE",!closed.IsVisible);
        owner.Close();app.Shutdown();
    }
}
