using Microsoft.Web.WebView2.WinForms;

namespace ClaudeBar;

/// <summary>
/// Fenêtre de connexion : une WebView2 visible sur claude.ai, qui partage le profil (cookies)
/// de la WebView cachée. Se ferme toute seule dès que l'API répond avec un compte connecté.
/// Remplace le « connecte-toi dans Safari » de la version macOS.
/// </summary>
internal sealed class LoginForm : Form
{
    private readonly WebView2 _web;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    private bool _checking;
    private bool _done;

    public bool LoggedIn { get; private set; }

    public LoginForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Connexion à claude.ai — Claude Bar";
        Icon = AppIcon.Icon;
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(500, 720);
        MinimumSize = new Size(380, 460);

        var banner = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 46,
            Padding = new Padding(12, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(0xFD, 0xF3, 0xE7),
            ForeColor = Color.FromArgb(0x5A, 0x3A, 0x10),
            Text = "Connecte-toi à ton compte Claude. Cette fenêtre se fermera automatiquement une fois connecté.",
        };

        _web = new WebView2 { Dock = DockStyle.Fill };

        Controls.Add(_web);
        Controls.Add(banner);
        ResumeLayout(false);

        _poll.Tick += async (_, _) => await CheckLoggedInAsync();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Sur un petit écran à 150 % (ex. 1920×1080), la taille par défaut dépasse : on la borne.
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(Width, wa.Width - 40), Math.Min(Height, wa.Height - 40));
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        try
        {
            var env = await WebViewEnvironment.GetAsync();
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.Navigate(ClaudeApi.BaseUrl + "login");
            _poll.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Ouverture de la fenêtre de connexion impossible", ex);
            MessageBox.Show(this,
                "Impossible d'ouvrir la fenêtre de connexion.\n\n" + ex.Message,
                "Claude Bar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task CheckLoggedInAsync()
    {
        if (_checking || _done || IsDisposed || _web.CoreWebView2 == null) return;
        _checking = true;
        try
        {
            var core = _web.CoreWebView2;
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) ||
                !uri.Host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string path = uri.AbsolutePath;
            if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/logout", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/magic-link", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cookies = await core.CookieManager.GetCookiesAsync("https://claude.ai");
            if (!cookies.Any(c => c.Name == "sessionKey" && !string.IsNullOrEmpty(c.Value))) return;

            var response = await ClaudeApi.GetAsync(core, "/api/organizations", TimeSpan.FromSeconds(10));
            var orgs = response.Status == 200 ? ClaudeApi.ParseOrganizations(response.Body) : null;
            if (orgs is { Count: > 0 } && !_done && !IsDisposed)
            {
                _done = true;
                LoggedIn = true;
                _poll.Stop();
                Log.Info("Connexion claude.ai réussie via la fenêtre intégrée");
                Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Vérification de connexion", ex);
        }
        finally
        {
            _checking = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
