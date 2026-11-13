namespace ClaudeBar;

internal static class Program
{
    private const string MutexName = @"Local\ClaudeBar.SingleInstance";
    internal const string ShowEventName = @"Local\ClaudeBar.ShowPopup";
    internal const string QuitEventName = @"Local\ClaudeBar.Quit";

    [STAThread]
    private static int Main(string[] args)
    {
        bool HasArg(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

        // Utilisé par l'installateur / désinstallateur pour fermer proprement l'instance en cours.
        if (HasArg("--quit"))
        {
            SignalEvent(QuitEventName);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Vérification automatique (CI) : ClaudeBar.exe --selftest <dossier>
        int selfTestIndex = Array.FindIndex(args, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
        if (selfTestIndex >= 0)
        {
            string outDir = selfTestIndex + 1 < args.Length ? args[selfTestIndex + 1] : Path.Combine(AppPaths.DataDir, "selftest");
            return SelfTest.Run(outDir);
        }

        bool launchedAtStartup = HasArg("--autostart") || PackageInfo.WasLaunchedByStartupTask();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Déjà lancé : on demande à l'instance existante d'afficher sa fenêtre.
            if (!launchedAtStartup)
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                SignalEvent(ShowEventName);
            }
            return 0;
        }

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Exception (thread UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Exception non gérée", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Exception de tâche non observée", e.Exception);
            e.SetObserved();
        };

        Log.Info($"Claude Bar {AppInfo.Version} démarre (autostart={launchedAtStartup})");
        Application.Run(new TrayApplicationContext(showSignal, quitSignal, launchedAtStartup));
        return 0;
    }

    private static void SignalEvent(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Aucune instance en cours.
        }
        catch (Exception ex)
        {
            Log.Error($"Impossible de signaler {name}", ex);
        }
    }
}
