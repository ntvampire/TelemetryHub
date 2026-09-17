using Microsoft.UI.Xaml;
using System.IO;

namespace KsitalTelemetryHub.UI.WinUI;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }
    public static string DatabasePath { get; set; } = "telemetry.db";

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Поиск БД рядом с исполняемым файлом или в рабочем каталоге
        if (!File.Exists(DatabasePath))
        {
            var appDir = System.AppContext.BaseDirectory;
            var localDb = Path.Combine(appDir, "telemetry.db");
            if (File.Exists(localDb))
            {
                DatabasePath = localDb;
            }
        }

        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
