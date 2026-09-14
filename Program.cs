namespace CrossMgrInterface;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        var demo = DemoLaunch.Parse(args, out var error);
        if (error != null)
        {
            // Not quietly the normal application instead: that would open the
            // real race database for someone who asked for a demo.
            MessageBox.Show(error, "CrossMgr demo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (demo == null)
        {
            Application.Run(new Form1());
            return;
        }

        // Before the window exists: it opens the database and settings as it is built.
        using var sandbox = DemoSandbox.Create(DemoLaunch.DemosFolder, demo.Scenario.Id, DateTime.Now);
        AppPaths.UseRoot(sandbox.Folder);
        Application.Run(new Form1(demo));
    }
}
