namespace KioskAdmin;

public static class Program
{
    static string CrashPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskAdmin", "crash.log");

    static void LogCrash(string where, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashPath)!);
            System.IO.File.AppendAllText(CrashPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{where}]  build {KioskShared.BuildInfo.Revision}\r\n{ex}\r\n\r\n");
        }
        catch { }
    }

    [STAThread]
    public static void Main()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        // Never crash silently: write what went wrong to crash.log (next to the admin app's data) and keep running when possible.
        app.DispatcherUnhandledException += (_, e) =>
        {
            LogCrash("UI thread", e.Exception);
            e.Handled = true;
            System.Windows.MessageBox.Show("Something went wrong, but the app keeps running.\n\n" + e.Exception.Message +
                "\n\nDetails are in: " + CrashPath, "Kiosk Admin", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("Fatal", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { LogCrash("Background task", e.Exception); e.SetObserved(); };

        Theme.Apply(app);
        GitHubUpdate.CleanupOld();
        var data = AdminData.Load();
        var role = LockRole.Admin;
        if (!string.IsNullOrEmpty(data.LockHash))
        {
            var lockWin = new LockWindow(data.LockHash, data.UserLockHash);
            if (lockWin.ShowDialog() != true) return;
            role = lockWin.Role;
        }

        app.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        if (role == LockRole.User) app.Run(new SimpleWindow(data));
        else app.Run(new MainWindow());
    }
}
