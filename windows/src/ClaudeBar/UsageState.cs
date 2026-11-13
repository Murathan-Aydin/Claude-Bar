namespace ClaudeBar;

internal enum ErrorKind
{
    None,
    NotLoggedIn,
    Network,
    Cloudflare,
    NoUsageData,
    WebViewMissing,
    Unknown,
}

internal sealed record Organization(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record UsageSnapshot(int SessionPercent, int WeeklyPercent, DateTimeOffset? SessionResetAt);

/// <summary>Équivalent Windows de UsageStore (macOS) : état partagé entre l'icône et la fenêtre.</summary>
internal sealed class UsageState
{
    public int SessionPercent { get; set; }
    public int WeeklyPercent { get; set; }
    public DateTimeOffset? SessionResetAt { get; set; }
    public DateTime? LastUpdated { get; set; }
    public bool IsLoading { get; set; } = true;
    public ErrorKind Error { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<Organization> Organizations { get; set; } = [];
    public string? CurrentOrgName { get; set; }

    public bool HasData => LastUpdated != null;

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public void ClearData()
    {
        SessionPercent = 0;
        WeeklyPercent = 0;
        SessionResetAt = null;
        LastUpdated = null;
        Organizations = [];
        CurrentOrgName = null;
    }
}

internal static class Messages
{
    public const string NotLoggedIn = "Connecte-toi à claude.ai pour afficher ton utilisation.";
    public const string Network = "Impossible de joindre claude.ai. Vérifie ta connexion internet.";
    public const string Cloudflare = "claude.ai ne répond pas comme prévu (vérification Cloudflare ?). Ouvre la fenêtre claude.ai pour la valider.";
    public const string WebViewMissing = "Le composant Microsoft Edge WebView2 est nécessaire. Installe-le puis relance Claude Bar.";

    public static string NoUsageData(string orgName) =>
        $"Aucune donnée d'utilisation pour l'espace « {orgName} ». Choisis un autre espace dans les paramètres.";
}
