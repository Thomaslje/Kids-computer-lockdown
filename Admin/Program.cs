namespace KioskAdmin;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

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
