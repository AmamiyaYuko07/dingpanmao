using System.IO;
using System.Windows;
using DingPanMao.Config;

namespace DingPanMao;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConfig.SettingsFolder,
        "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>把未处理异常写到日志，方便排查问题。</summary>
    public static void Log(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.AppendAllText(
                LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 日志写不进去就算了。
        }
    }
}
