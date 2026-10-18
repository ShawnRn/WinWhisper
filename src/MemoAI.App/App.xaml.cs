using Microsoft.UI.Xaml;

namespace MemoAI.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public App()
    {
        UnhandledException += (_, e) =>
        {
            var root = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--data-root="))?[12..]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoAI.Native");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "crash.log"), $"{DateTimeOffset.Now:O}\n{e.Exception}\n");
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        if (Environment.GetCommandLineArgs().Contains("--ui-self-test")) MainWindow.AppWindow.Show(false);
        else MainWindow.Activate();
    }
}
