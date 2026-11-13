using Microsoft.Win32;

namespace ClaudeBar;

/// <summary>
/// Équivalent Windows de l'AppDelegate macOS : icône dans la zone de notification, fenêtre au clic,
/// WebView2 cachée pour les appels API, rafraîchissement toutes les 5 minutes.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly UsageState _state = new();
    private readonly Form _host;
    private readonly UsageService _service;
    private readonly NotifyIcon _tray;
    private readonly UsagePopup _popup;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _quitWait;

    private Icon? _trayIcon;
    private bool _refreshing;
    private bool _refreshQueued;
    private bool _queuedForce;
    private bool _exiting;
    private SettingsForm? _settingsForm;
    private LoginForm? _loginForm;

    public TrayApplicationContext(EventWaitHandle showSignal, EventWaitHandle quitSignal, bool launchedAtStartup)
    {
        _settings = AppSettings.Load();
        StartupManager.RefreshPathIfEnabled();

        // Fenêtre jamais affichée : parent de la WebView2 cachée + accès au thread UI.
        _host = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1280, 800),
        };
        _ = _host.Handle;

        _service = new UsageService(_host);

        _popup = new UsagePopup(_state);
        _popup.RefreshClicked += () => _ = RefreshAsync(force: true);
        _popup.SettingsClicked += () => { _popup.HidePopup(); OpenSettings(); };
        _popup.LoginClicked += () => { _popup.HidePopup(); OpenLogin(); };
        _popup.OpenClaudeClicked += () => { _popup.HidePopup(); Shell.OpenUrl(ClaudeApi.BaseUrl); };
        _popup.QuitClicked += ExitApp;
        _popup.InstallWebViewClicked += () => { _popup.HidePopup(); Shell.OpenUrl(WebView2DownloadUrl); };

        _autostartItem = new ToolStripMenuItem("Lancer au démarrage de Windows");
        _tray = new NotifyIcon
        {
            Text = "Claude Bar",
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        _tray.MouseClick += OnTrayClick;
        UpdateTray();

        _timer = new System.Windows.Forms.Timer { Interval = (int)RefreshInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => _ = RefreshAsync(force: false);
        _timer.Start();

        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => PostToUi(ShowPopupNearTray), null, Timeout.Infinite, executeOnlyOnce: false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(quitSignal,
            (_, _) => PostToUi(ExitApp), null, Timeout.Infinite, executeOnlyOnce: false);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        PostToUi(() => _ = StartupAsync(launchedAtStartup));
    }

    private void PostToUi(Action action)
    {
        try
        {
            if (!_exiting && !_host.IsDisposed) _host.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Fenêtre détruite pendant la fermeture.
        }
    }

    private async Task StartupAsync(bool launchedAtStartup)
    {
        if (!WebViewEnvironment.IsRuntimeAvailable(out var version))
        {
            _state.IsLoading = false;
            _state.Error = ErrorKind.WebViewMissing;
            _state.ErrorMessage = Messages.WebViewMissing;
            _state.NotifyChanged();
            UpdateTray();
            if (!launchedAtStartup)
            {
                var answer = MessageBox.Show(
                    "Claude Bar a besoin du composant Microsoft Edge WebView2 (déjà présent sur la plupart des PC Windows 10/11).\n\nOuvrir la page de téléchargement ?",
                    "Claude Bar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes) Shell.OpenUrl(WebView2DownloadUrl);
            }
            return;
        }
        Log.Info($"WebView2 runtime {version}");

        await StartupManager.IsEnabledAsync();
        bool firstRun = !_settings.HasCompletedFirstRun;
        if (firstRun)
        {
            _settings.HasCompletedFirstRun = true;
            if (PackageInfo.IsPackaged) await StartupManager.SetEnabledAsync(true);
            _settings.Save();
            _tray.ShowBalloonTip(8000, "Claude Bar est lancé",
                "L'icône est dans la zone de notification, près de l'horloge. Astuce : si elle est cachée derrière « ^ », fais-la glisser dans la barre des tâches pour la garder visible.",
                ToolTipIcon.Info);
        }

        await RefreshAsync(force: true);

        if (_state.Error == ErrorKind.NotLoggedIn && (firstRun || !launchedAtStartup))
        {
            OpenLogin();
        }
        else if (!firstRun && !launchedAtStartup)
        {
            ShowPopupNearTray();
        }
    }

    // ── Rafraîchissement ───────────────────────────────────────────────────

    private async Task RefreshAsync(bool force)
    {
        if (_exiting) return;
        if (_refreshing)
        {
            _refreshQueued = true;
            _queuedForce |= force;
            return;
        }

        _refreshing = true;
        _state.IsLoading = true;
        _state.Error = ErrorKind.None;
        _state.ErrorMessage = null;
        _state.NotifyChanged();
        UpdateTray();

        try
        {
            var result = await _service.FetchAsync(_settings.SelectedOrgId, force);
            if (result.Organizations.Count > 0) _state.Organizations = result.Organizations;

            if (result.Error == ErrorKind.None && result.Usage is { } usage)
            {
                _state.SessionPercent = usage.SessionPercent;
                _state.WeeklyPercent = usage.WeeklyPercent;
                _state.SessionResetAt = usage.SessionResetAt;
                _state.CurrentOrgName = result.Organization?.Name;
                _state.LastUpdated = DateTime.Now;
                Log.Info($"Utilisation : session {usage.SessionPercent}%, semaine {usage.WeeklyPercent}%");
            }
            else
            {
                if (result.Error == ErrorKind.NotLoggedIn) _state.ClearData();
                _state.Error = result.Error;
                _state.ErrorMessage = result.Message;
            }
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            _state.Error = ErrorKind.WebViewMissing;
            _state.ErrorMessage = Messages.WebViewMissing;
        }
        catch (Exception ex)
        {
            Log.Error("Rafraîchissement impossible", ex);
            _state.Error = ErrorKind.Unknown;
            _state.ErrorMessage = $"Erreur inattendue : {ex.Message}";
        }
        finally
        {
            _state.IsLoading = false;
            _refreshing = false;
            if (!_exiting)
            {
                _state.NotifyChanged();
                UpdateTray();
            }
        }

        if (_refreshQueued && !_exiting)
        {
            bool queuedForce = _queuedForce;
            _refreshQueued = false;
            _queuedForce = false;
            await RefreshAsync(queuedForce);
        }
    }

    private void UpdateTray()
    {
        if (_exiting) return;

        Icon icon;
        string tooltip;
        if (_state.Error == ErrorKind.NotLoggedIn)
        {
            icon = TrayIconRenderer.RenderLoggedOut();
            tooltip = "Claude Bar\nNon connecté à claude.ai";
        }
        else if (_state.HasData)
        {
            icon = TrayIconRenderer.RenderPercent(_state.SessionPercent);
            string reset = _state.SessionResetAt is { } r ? $" (refresh {r.ToLocalTime():HH:mm})" : "";
            tooltip = $"Claude Bar\nSession : {_state.SessionPercent} %{reset}\nSemaine : {_state.WeeklyPercent} %";
            if (_state.Error != ErrorKind.None) tooltip += "\n⚠ Dernière mise à jour en échec";
        }
        else if (_state.Error != ErrorKind.None)
        {
            icon = TrayIconRenderer.RenderError();
            tooltip = "Claude Bar\n" + (_state.ErrorMessage ?? "Erreur");
        }
        else
        {
            icon = TrayIconRenderer.RenderLoading();
            tooltip = "Claude Bar\nChargement…";
        }

        var previous = _trayIcon;
        _tray.Icon = icon;
        _trayIcon = icon;
        previous?.Dispose();
        _tray.Text = tooltip.Length > 127 ? tooltip[..126] + "…" : tooltip;
    }

    // ── Icône & menu ───────────────────────────────────────────────────────

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_popup.Visible)
        {
            _popup.HidePopup();
        }
        else if (!_popup.WasJustHidden)
        {
            ShowPopup(Cursor.Position);
        }
    }

    private void ShowPopupNearTray()
    {
        // Ouverture sans clic (relance depuis le menu Démarrer) : coin de la zone de notification.
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        var bounds = Screen.PrimaryScreen?.Bounds ?? wa;
        var anchor = wa.Top > bounds.Top ? new Point(wa.Right - 1, wa.Top)
            : wa.Left > bounds.Left ? new Point(wa.Left, wa.Bottom - 1)
            : new Point(wa.Right - 1, wa.Bottom - 1);
        ShowPopup(anchor);
    }

    private void ShowPopup(Point anchor)
    {
        if (_exiting) return;
        _popup.ShowNear(anchor);

        // Données de plus d'une minute : on les affiche tout de suite et on rafraîchit en arrière-plan.
        if (!_refreshing && (_state.LastUpdated is not { } last || DateTime.Now - last > TimeSpan.FromMinutes(1)))
        {
            _ = RefreshAsync(force: false);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher l'utilisation", null, (_, _) => ShowPopupNearTray());
        menu.Items.Add("Rafraîchir", null, (_, _) => _ = RefreshAsync(force: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Se connecter à claude.ai…", null, (_, _) => OpenLogin());
        menu.Items.Add("Paramètres…", null, (_, _) => OpenSettings());
        menu.Items.Add("Ouvrir claude.ai", null, (_, _) => Shell.OpenUrl(ClaudeApi.BaseUrl));
        menu.Items.Add(new ToolStripSeparator());
        _autostartItem.Click += async (_, _) =>
        {
            string? error = await StartupManager.SetEnabledAsync(!StartupManager.CachedEnabled);
            if (error != null) _tray.ShowBalloonTip(6000, "Claude Bar", error, ToolTipIcon.Info);
        };
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            _popup.HidePopup();
            _autostartItem.Checked = StartupManager.CachedEnabled;
            _ = UpdateAutostartCheckAsync();
        };
        return menu;
    }

    private async Task UpdateAutostartCheckAsync() =>
        _autostartItem.Checked = await StartupManager.IsEnabledAsync();

    // ── Fenêtres ───────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        var form = new SettingsForm(_settings, _state);
        form.LoginRequested += OpenLogin;
        form.SessionKeyEntered += key => _ = ApplySessionKeyAsync(key);
        form.SessionCleared += () => _ = ClearSessionAsync();
        form.OrganizationChanged += () => _ = RefreshAsync(force: false);
        form.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm = form;
        form.Show();
        form.Activate();
    }

    private void OpenLogin()
    {
        if (_loginForm is { IsDisposed: false })
        {
            _loginForm.Activate();
            return;
        }

        var form = new LoginForm();
        form.FormClosed += async (_, _) =>
        {
            _loginForm = null;
            if (form.LoggedIn && !_exiting)
            {
                await RefreshAsync(force: true);
                ShowPopupNearTray();
            }
        };
        _loginForm = form;
        form.Show();
        form.Activate();
    }

    private async Task ApplySessionKeyAsync(string key)
    {
        try
        {
            await _service.SetSessionKeyAsync(key);
        }
        catch (Exception ex)
        {
            Log.Error("Enregistrement de la sessionKey impossible", ex);
        }
        await RefreshAsync(force: true);
    }

    private async Task ClearSessionAsync()
    {
        try
        {
            await _service.ResetSessionAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Effacement de la session impossible", ex);
        }
        _state.ClearData();
        await RefreshAsync(force: true);
    }

    // ── Événements système ─────────────────────────────────────────────────

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        PostToUi(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8)); // laisse le réseau revenir après la veille
            await RefreshAsync(force: true);
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => PostToUi(UpdateTray);

    // ── Fermeture ──────────────────────────────────────────────────────────

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Fermeture");

        _showWait.Unregister(null);
        _quitWait.Unregister(null);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Stop();

        _tray.Visible = false;
        _settingsForm?.Close();
        _loginForm?.Close();
        _popup.Close();
        _service.Dispose();

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _trayIcon?.Dispose();
            _popup.Dispose();
            _host.Dispose();
        }
        base.Dispose(disposing);
    }
}
