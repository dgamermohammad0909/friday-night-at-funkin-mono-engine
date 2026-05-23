using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI;

namespace FNF_MonoGame;

sealed partial class App : Application
{
    public static string LaunchArgs { get; private set; } = "";

    public App()
    {
        InitializeComponent();
        Suspending += OnSuspending;
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LaunchArgs = args.Arguments ?? "";

        // On Xbox, the PLM requires activation ASAP.
        // GamePage has a dark background set in XAML, so it renders immediately
        // even before MonoGame's DirectX initialization completes.
        if (Window.Current.Content == null)
        {
            var page = new GamePage();
            Window.Current.Content = page;
        }
        Window.Current.Activate();
    }

    private void OnSuspending(object sender, SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        deferral.Complete();
    }

    private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            var tb = new TextBlock
            {
                Text = $"UNHANDLED:\n{e.Exception?.GetType().Name}: {e.Message}\n\n{e.Exception?.StackTrace}",
                Foreground = new SolidColorBrush(Colors.Red),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
            Window.Current.Content = new ScrollViewer { Content = tb };
        }
        catch { }
    }
}
