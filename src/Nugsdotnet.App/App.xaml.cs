namespace Nugsdotnet.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage())
        {
            Title = "nugsdotnet",
            Width = 1400,
            Height = 900,
            // Dark title bar that matches the RECEIVER '74 cabinet. The MAUI
            // TitleBar control (Windows + Mac Catalyst) keeps the native
            // min/max/close buttons; only the bar fill + title text are ours.
            // Colors mirror --surface / --text in app.css.
            TitleBar = new TitleBar
            {
                Title = "nugsdotnet",
                BackgroundColor = Color.FromArgb("#15120D"),
                ForegroundColor = Color.FromArgb("#EFE4CF"),
            },
        };
    }
}
