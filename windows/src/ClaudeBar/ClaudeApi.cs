using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ClaudeBar;

/// <summary>Environnement WebView2 partagé : la WebView cachée et la fenêtre de connexion
/// utilisent le même profil, donc les mêmes cookies claude.ai.</summary>
internal static class WebViewEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    public static Task<CoreWebView2Environment> GetAsync()
    {
        if (_environment == null || _environment.IsFaulted || _environment.IsCanceled)
        {
            Directory.CreateDirectory(AppPaths.WebViewDataDir);
            _environment = CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir);
        }
        return _environment;
    }

    public static bool IsRuntimeAvailable(out string? version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch (Exception ex)
        {
            Log.Error("Runtime WebView2 introuvable", ex);
            version = null;
            return false;
        }
    }
}

internal sealed record ApiResponse(int Status, string Body, bool ScriptFailed)
{
    public bool IsJson
    {
        get
        {
            var t = Body.TrimStart();
            return t.StartsWith('{') || t.StartsWith('[');
        }
    }
}

/// <summary>Appels aux API internes de claude.ai exécutés DANS la page (fetch same-origin),
/// exactement comme callAsyncJavaScript côté macOS : ça passe Cloudflare et réutilise les cookies.</summary>
internal static class ClaudeApi
{
    public const string BaseUrl = "https://claude.ai/";

    public static async Task<ApiResponse> GetAsync(CoreWebView2 web, string path, TimeSpan? timeout = null)
    {
        // JsonSerializer produit un littéral de chaîne valide en JavaScript (pas d'injection possible).
        string js =
            "(async () => {" +
            "  try {" +
            $"    const r = await fetch({JsonSerializer.Serialize(path)}, {{ credentials: 'include', headers: {{ 'Accept': 'application/json' }} }});" +
            "    return JSON.stringify({ status: r.status, body: await r.text() });" +
            "  } catch (e) {" +
            "    return JSON.stringify({ status: 0, body: String(e) });" +
            "  }" +
            "})()";

        string? raw = await EvaluateStringAsync(web, js, timeout ?? TimeSpan.FromSeconds(20));
        if (raw == null) return new ApiResponse(0, "", ScriptFailed: true);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            int status = doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
            string body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new ApiResponse(status, body, ScriptFailed: false);
        }
        catch (JsonException)
        {
            return new ApiResponse(0, "", ScriptFailed: true);
        }
    }

    /// <summary>Runtime.evaluate (DevTools Protocol) avec awaitPromise : WebView2 ne sait pas
    /// attendre une Promise via ExecuteScriptAsync.</summary>
    private static async Task<string?> EvaluateStringAsync(CoreWebView2 web, string expression, TimeSpan timeout)
    {
        string parameters = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true });

        Task<string> call;
        try
        {
            call = web.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters);
        }
        catch (Exception ex)
        {
            Log.Error("Runtime.evaluate n'a pas pu démarrer", ex);
            return null;
        }

        var finished = await Task.WhenAny(call, Task.Delay(timeout));
        if (finished != call)
        {
            Log.Error("Runtime.evaluate : délai dépassé");
            _ = call.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return null;
        }

        string json;
        try
        {
            json = await call;
        }
        catch (Exception ex)
        {
            // Typiquement : contexte d'exécution détruit pendant une navigation (challenge Cloudflare).
            Log.Error("Runtime.evaluate a échoué", ex);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exceptionDetails", out _)) return null;
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException ex)
        {
            Log.Error("Réponse DevTools illisible", ex);
        }
        return null;
    }

    public static List<Organization>? ParseOrganizations(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var orgs = new List<Organization>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("uuid", out var id) || id.ValueKind != JsonValueKind.String) continue;
                string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? "Sans nom"
                    : "Sans nom";
                orgs.Add(new Organization(id.GetString()!, name));
            }
            return orgs;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Même logique que fetchUsageViaJS (macOS) : five_hour / seven_day / utilization.</summary>
    public static UsageSnapshot? ParseUsage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            int session = 0, weekly = 0;
            DateTimeOffset? resetAt = null;
            bool found = false;

            if (root.TryGetProperty("five_hour", out var fiveHour) && fiveHour.ValueKind == JsonValueKind.Object)
            {
                found = true;
                session = ReadPercent(fiveHour);
                foreach (var key in new[] { "resets_at", "reset_at", "resets", "reset" })
                {
                    if (!fiveHour.TryGetProperty(key, out var v)) continue;
                    if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
                    {
                        resetAt = d;
                        break;
                    }
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n))
                    {
                        resetAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(n > 1e12 ? n : n * 1000));
                        break;
                    }
                }
            }

            if (root.TryGetProperty("seven_day", out var sevenDay) && sevenDay.ValueKind == JsonValueKind.Object)
            {
                found = true;
                weekly = ReadPercent(sevenDay);
            }

            return found ? new UsageSnapshot(session, weekly, resetAt) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadPercent(JsonElement obj) =>
        obj.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetDouble(out var d)
            ? Math.Clamp((int)d, 0, 100)
            : 0;
}
