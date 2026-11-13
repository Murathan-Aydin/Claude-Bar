using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace ClaudeBar;

/// <summary>Distingue la version Microsoft Store (paquet MSIX) de la version installateur classique.</summary>
internal static class PackageInfo
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Vrai si Windows a lancé l'app via sa tâche de démarrage (version Store).</summary>
    public static bool WasLaunchedByStartupTask()
    {
        if (!IsPackaged) return false;
        try
        {
            return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Lancement au démarrage de Windows.
/// - Version Store : tâche de démarrage déclarée dans le manifeste MSIX (API StartupTask), seule méthode
///   autorisée pour une app du Store ; l'utilisateur la voit dans Paramètres > Applications > Démarrage.
/// - Version installateur : clé HKCU\...\Run (aucun droit administrateur requis).
/// </summary>
internal static class StartupManager
{
    private const string TaskId = "ClaudeBarStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeBar";

    /// <summary>Dernier état connu (pour cocher le menu sans attendre).</summary>
    public static bool CachedEnabled { get; private set; }

    private static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    public static async Task<bool> IsEnabledAsync(bool throwOnError = false)
    {
        try
        {
            if (PackageInfo.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                CachedEnabled = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                CachedEnabled = key?.GetValue(ValueName) is string s && s.Length > 0;
            }
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log.Error("Lecture de l'état du démarrage automatique impossible", ex);
        }
        return CachedEnabled;
    }

    /// <summary>Active ou désactive. Renvoie un message à afficher si Windows refuse le changement.</summary>
    public static async Task<string?> SetEnabledAsync(bool enabled)
    {
        try
        {
            if (PackageInfo.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (enabled)
                {
                    var state = await task.RequestEnableAsync();
                    CachedEnabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                    if (state == StartupTaskState.DisabledByUser)
                    {
                        return "Le démarrage automatique a été désactivé dans Windows. Réactive-le dans Paramètres > Applications > Démarrage.";
                    }
                    if (state == StartupTaskState.DisabledByPolicy)
                    {
                        return "Le démarrage automatique est bloqué par une stratégie de ton organisation.";
                    }
                }
                else
                {
                    task.Disable();
                    CachedEnabled = false;
                }
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(ValueName, Command);
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
                CachedEnabled = enabled;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Modification du lancement au démarrage impossible", ex);
            return "Impossible de modifier le lancement au démarrage.";
        }
        return null;
    }

    /// <summary>Version installateur : si l'app a été déplacée, on remet à jour le chemin enregistré.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (PackageInfo.IsPackaged) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is string current && current.Length > 0 &&
                !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            {
                using var writable = Registry.CurrentUser.CreateSubKey(RunKey);
                writable.SetValue(ValueName, Command);
            }
        }
        catch
        {
            // ignore
        }
    }
}
