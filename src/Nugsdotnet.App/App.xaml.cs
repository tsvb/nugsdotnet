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
        };
    }
}
