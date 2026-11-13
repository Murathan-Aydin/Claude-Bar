using Microsoft.Web.WebView2.Core;

namespace ClaudeBar;

internal sealed record FetchResult(
    UsageSnapshot? Usage,
    IReadOnlyList<Organization> Organizations,
    Organization? Organization,
    ErrorKind Error,
    string? Message)
{
    public static FetchResult Ok(UsageSnapshot usage, IReadOnlyList<Organization> orgs, Organization org) =>
        new(usage, orgs, org, ErrorKind.None, null);

    public static FetchResult Fail(ErrorKind kind, string message, IReadOnlyList<Organization>? orgs = null) =>
        new(null, orgs ?? [], null, kind, message);
}

/// <summary>
/// Équivalent Windows du WKWebView caché de l'AppDelegate macOS : une WebView2 invisible charge
/// claude.ai (cookies Cloudflare), puis appelle /api/organizations et /api/organizations/{id}/usage.
/// </summary>
internal sealed class UsageService : IDisposable
{
    private static readonly CoreWebView2WebErrorStatus[] NetworkErrors =
    [
        CoreWebView2WebErrorStatus.CannotConnect,
        CoreWebView2WebErrorStatus.HostNameNotResolved,
        CoreWebView2WebErrorStatus.Disconnected,
        CoreWebView2WebErrorStatus.ConnectionAborted,
        CoreWebView2WebErrorStatus.ConnectionReset,
        CoreWebView2WebErrorStatus.ServerUnreachable,
        CoreWebView2WebErrorStatus.Timeout,
    ];

    private readonly Control _host;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _web;

    public UsageService(Control host) => _host = host;

    private async Task<CoreWebView2> EnsureWebViewAsync()
    {
        if (_web != null) return _web;

        var env = await WebViewEnvironment.GetAsync();
        _controller = await env.CreateCoreWebView2ControllerAsync(_host.Handle);
        _controller.Bounds = new Rectangle(0, 0, 1280, 800);
        _controller.IsVisible = true; // la fenêtre hôte, elle, n'est jamais affichée

        _web = _controller.CoreWebView2;
        _web.Settings.AreDefaultScriptDialogsEnabled = false;
        _web.Settings.IsStatusBarEnabled = false;
        _web.IsMuted = true;
        Log.Info("WebView2 cachée initialisée");
        return _web;
    }

    public async Task<FetchResult> FetchAsync(string? preferredOrgId, bool forceReload)
    {
        var web = await EnsureWebViewAsync();

        bool navigated = false;
        if (forceReload || !IsOnClaude(web.Source))
        {
            var navError = await NavigateToClaudeAsync(web);
            navigated = true;
            if (navError is { } status && NetworkErrors.Contains(status))
            {
                Log.Error($"Navigation claude.ai : {status}");
                return FetchResult.Fail(ErrorKind.Network, Messages.Network);
            }
        }

        // 1) Organisations — on réessaie tant que la page n'est pas prête (challenge Cloudflare, chargement…)
        ApiResponse? orgResponse = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            orgResponse = await ClaudeApi.GetAsync(web, "/api/organizations");
            if (orgResponse.IsJson) break;

            Log.Info($"/api/organizations pas encore prêt (tentative {attempt + 1}, HTTP {orgResponse.Status})");
            if (!navigated)
            {
                // La page restée ouverte a peut-être perdu sa validation Cloudflare : on la recharge.
                await NavigateToClaudeAsync(web);
                navigated = true;
                continue;
            }
            await Task.Delay(2000);
        }

        if (orgResponse is not { IsJson: true })
        {
            return orgResponse is { Status: 0, ScriptFailed: false }
                ? FetchResult.Fail(ErrorKind.Network, Messages.Network)
                : FetchResult.Fail(ErrorKind.Cloudflare, Messages.Cloudflare);
        }

        var orgs = ClaudeApi.ParseOrganizations(orgResponse.Body);
        if (orgs == null || orgs.Count == 0)
        {
            Log.Info($"/api/organizations → HTTP {orgResponse.Status}, non connecté");
            return FetchResult.Fail(ErrorKind.NotLoggedIn, Messages.NotLoggedIn);
        }

        // 2) Utilisation — l'espace choisi dans les paramètres, sinon le premier qui a des données.
        var saved = orgs.FirstOrDefault(o => o.Id == preferredOrgId);
        List<Organization> candidates = saved != null ? [saved] : orgs.Take(4).ToList();

        foreach (var org in candidates)
        {
            var usageResponse = await ClaudeApi.GetAsync(web, $"/api/organizations/{Uri.EscapeDataString(org.Id)}/usage");
            Log.Info($"/usage ({org.Name}) → HTTP {usageResponse.Status}");

            var usage = usageResponse.Status == 200 ? ClaudeApi.ParseUsage(usageResponse.Body) : null;
            if (usage != null)
            {
                return FetchResult.Ok(usage, orgs, org);
            }
        }

        var shown = candidates[0];
        return FetchResult.Fail(ErrorKind.NoUsageData, Messages.NoUsageData(shown.Name), orgs);
    }

    /// <summary>Remplace le cookie sessionKey (saisie manuelle, ex. utilisateurs Chrome).</summary>
    public async Task SetSessionKeyAsync(string sessionKey)
    {
        var web = await EnsureWebViewAsync();
        var cookies = web.CookieManager;
        cookies.DeleteCookies("sessionKey", "https://claude.ai");

        var cookie = cookies.CreateCookie("sessionKey", sessionKey, ".claude.ai", "/");
        cookie.IsSecure = true;
        cookie.IsHttpOnly = true;
        cookie.SameSite = CoreWebView2CookieSameSiteKind.Lax;
        cookie.Expires = DateTime.Now.AddYears(1);
        cookies.AddOrUpdateCookie(cookie);
        Log.Info("sessionKey manuelle enregistrée");
    }

    /// <summary>Déconnexion : purge cookies + cache du profil WebView2 (dédié à l'app).</summary>
    public async Task ResetSessionAsync()
    {
        var web = await EnsureWebViewAsync();
        web.CookieManager.DeleteAllCookies();
        try
        {
            await web.Profile.ClearBrowsingDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error("ClearBrowsingDataAsync a échoué", ex);
        }
        web.Navigate("about:blank");
        Log.Info("Session claude.ai effacée");
    }

    private static bool IsOnClaude(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase);

    /// <summary>Navigue vers claude.ai et attend la fin du chargement (remplace le délai fixe de 4 s).</summary>
    private static async Task<CoreWebView2WebErrorStatus?> NavigateToClaudeAsync(CoreWebView2 web)
    {
        var tcs = new TaskCompletionSource<(bool Success, CoreWebView2WebErrorStatus Status)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
            tcs.TrySetResult((e.IsSuccess, e.WebErrorStatus));

        web.NavigationCompleted += OnCompleted;
        try
        {
            web.Navigate(ClaudeApi.BaseUrl);
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (finished != tcs.Task) return CoreWebView2WebErrorStatus.Timeout;

            var (success, status) = await tcs.Task;
            // Un 403 Cloudflare donne IsSuccess = false mais la page de challenge s'exécute quand même :
            // seules les vraies erreurs réseau sont remontées à l'appelant.
            return success ? null : status;
        }
        finally
        {
            web.NavigationCompleted -= OnCompleted;
        }
    }

    public void Dispose()
    {
        try
        {
            _controller?.Close();
        }
        catch
        {
            // ignore
        }
        _controller = null;
        _web = null;
    }
}
