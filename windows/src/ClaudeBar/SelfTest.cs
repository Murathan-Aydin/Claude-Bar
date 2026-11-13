using System.Drawing.Imaging;
using System.Text;

namespace ClaudeBar;

/// <summary>
/// ClaudeBar.exe --selftest &lt;dossier&gt; : vérification automatique sur une vraie machine Windows (CI).
/// 1) dessine l'icône et la fenêtre dans tous les états (PNG) ;
/// 2) initialise la WebView2 cachée et interroge claude.ai (sans compte : « non connecté » attendu).
/// Code de sortie 0 si tout fonctionne.
/// </summary>
internal static class SelfTest
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        report.AppendLine($"Claude Bar {AppInfo.Version} — selftest {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Windows {Environment.OSVersion.Version}");
        int exitCode = 0;

        try
        {
            RenderIcons(outDir);
            RenderPopupStates(outDir);
            report.AppendLine("Rendu icônes + fenêtre : OK");
        }
        catch (Exception ex)
        {
            report.AppendLine($"Rendu : ÉCHEC — {ex}");
            exitCode = 1;
        }

        if (!WebViewEnvironment.IsRuntimeAvailable(out var version))
        {
            report.AppendLine("WebView2 runtime : ABSENT");
            exitCode = 1;
        }
        else
        {
            report.AppendLine($"WebView2 runtime : {version}");
            var (result, error) = RunLiveFetch();
            if (error != null)
            {
                report.AppendLine($"Appel claude.ai : EXCEPTION — {error}");
                exitCode = 1;
            }
            else if (result == null)
            {
                report.AppendLine("Appel claude.ai : délai dépassé");
                exitCode = 1;
            }
            else
            {
                report.AppendLine($"Appel claude.ai : {result.Error} — {result.Message ?? "OK"}");
                if (result.Error is ErrorKind.Unknown or ErrorKind.WebViewMissing) exitCode = 1;
            }
        }

        report.AppendLine(exitCode == 0 ? "RÉSULTAT : OK" : "RÉSULTAT : ÉCHEC");
        File.WriteAllText(Path.Combine(outDir, "selftest.txt"), report.ToString());
        return exitCode;
    }

    private static void RenderIcons(string outDir)
    {
        var icons = new (string Name, Func<Icon> Make)[]
        {
            ("tray-08", () => TrayIconRenderer.RenderPercent(8)),
            ("tray-42", () => TrayIconRenderer.RenderPercent(42)),
            ("tray-75", () => TrayIconRenderer.RenderPercent(75)),
            ("tray-100", () => TrayIconRenderer.RenderPercent(100)),
            ("tray-loggedout", TrayIconRenderer.RenderLoggedOut),
            ("tray-loading", TrayIconRenderer.RenderLoading),
        };
        foreach (var (name, make) in icons)
        {
            using var icon = make();
            using var bmp = icon.ToBitmap();
            bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        }
    }

    private static void RenderPopupStates(string outDir)
    {
        var state = new UsageState();
        using var popup = new UsagePopup(state);

        var scenarios = new (string Name, Action Setup)[]
        {
            ("data", () =>
            {
                state.IsLoading = false;
                state.Error = ErrorKind.None;
                state.SessionPercent = 42;
                state.WeeklyPercent = 87;
                state.SessionResetAt = DateTimeOffset.Now.AddHours(2);
                state.LastUpdated = DateTime.Now.AddMinutes(-3);
                state.Organizations = [new Organization("a", "Perso"), new Organization("b", "OID Consultants")];
                state.CurrentOrgName = "OID Consultants";
            }),
            ("loading", () =>
            {
                state.ClearData();
                state.IsLoading = true;
                state.Error = ErrorKind.None;
            }),
            ("loggedout", () =>
            {
                state.IsLoading = false;
                state.Error = ErrorKind.NotLoggedIn;
                state.ErrorMessage = Messages.NotLoggedIn;
            }),
            ("cloudflare", () =>
            {
                state.Error = ErrorKind.Cloudflare;
                state.ErrorMessage = Messages.Cloudflare;
            }),
        };

        foreach (var (name, setup) in scenarios)
        {
            setup();
            foreach (var (theme, dark) in new[] { ("light", false), ("dark", true) })
            {
                foreach (int dpi in new[] { 96, 144 })
                {
                    using var bmp = popup.RenderSnapshot(dark, dpi);
                    bmp.Save(Path.Combine(outDir, $"popup-{name}-{theme}-{dpi}.png"), ImageFormat.Png);
                }
            }
        }

        // Fenêtre Paramètres (contrôles WinForms standard) : affichée hors écran le temps de la capture.
        scenarios[0].Setup();
        using var settings = new SettingsForm(new AppSettings { SelectedOrgId = "b" }, state)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ShowInTaskbar = false,
        };
        settings.Show();
        Application.DoEvents();
        using var shot = new Bitmap(settings.Width, settings.Height);
        settings.DrawToBitmap(shot, new Rectangle(Point.Empty, settings.Size));
        shot.Save(Path.Combine(outDir, "settings.png"), ImageFormat.Png);
        settings.Close();
    }

    private static (FetchResult? Result, Exception? Error) RunLiveFetch()
    {
        FetchResult? result = null;
        Exception? error = null;

        using var host = new Form { ShowInTaskbar = false, Size = new Size(1280, 800) };
        _ = host.Handle;
        using var service = new UsageService(host);
        using var watchdog = new System.Windows.Forms.Timer { Interval = 120_000 };
        watchdog.Tick += (_, _) => Application.ExitThread();
        watchdog.Start();

        host.BeginInvoke(new Action(async () =>
        {
            try
            {
                result = await service.FetchAsync(null, forceReload: true);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Application.ExitThread();
            }
        }));

        Application.Run();
        return (result, error);
    }
}
