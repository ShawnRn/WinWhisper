using Microsoft.UI.Xaml;

namespace MemoAI.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoAI.Native");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "crash.log"), $"{DateTimeOffset.Now:O}\n{e.Exception}\n");
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow(); MainWindow.Activate();
    }
}
