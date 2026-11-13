# Claude Bar — Windows

Version Windows de Claude Bar : affiche ton utilisation de Claude.ai (session de 5 h et limite hebdomadaire) dans la **zone de notification** de Windows, près de l'horloge.

![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4) ![.NET](https://img.shields.io/badge/.NET-10-512BD4)

---

## Installation (utilisateurs)

1. Télécharge **[ClaudeBar-Setup.exe](https://github.com/Murathan-Aydin/Claude-Bar/releases/latest/download/ClaudeBar-Setup.exe)**
2. Double-clique dessus → **Suivant** → **Terminer**
   - Aucun droit administrateur n'est demandé (installation dans ton profil).
3. Au premier lancement, une fenêtre **claude.ai** s'ouvre : connecte-toi. Elle se ferme toute seule une fois connecté.

C'est tout. L'icône affiche le pourcentage de la session en cours, sur fond **vert** (< 60 %), **orange** (< 90 %) ou **rouge**.

> **Message « Windows a protégé votre ordinateur » ?**
> L'installateur n'est pas signé numériquement (un certificat de signature est payant). Clique sur **Informations complémentaires** → **Exécuter quand même**.

> **Je ne vois pas l'icône ?**
> Windows 11 range les nouvelles icônes derrière la flèche **^** de la barre des tâches. Fais glisser l'icône Claude Bar hors de ce menu pour la garder visible. Relancer Claude Bar depuis le menu Démarrer ouvre aussi directement la fenêtre.

### Utilisation

| Action | Résultat |
|---|---|
| Clic gauche sur l'icône | Ouvre la fenêtre (session, semaine, heure de réinitialisation) |
| Clic droit sur l'icône | Menu : rafraîchir, se connecter, paramètres, démarrage auto, quitter |
| Survol de l'icône | Infobulle avec les deux pourcentages |
| `F5` / `Échap` dans la fenêtre | Rafraîchir / fermer |

Les données se rafraîchissent toutes les 5 minutes, à l'ouverture de la fenêtre (si elles ont plus d'une minute) et au réveil du PC.

### Paramètres (⚙)

- **Espace de travail** : si ton compte a plusieurs organisations (perso + équipe), choisis laquelle suivre.
- **Lancer au démarrage de Windows** : coché par défaut à l'installation.
- **Se connecter à claude.ai…** : rouvre la fenêtre de connexion (changement de compte).
- **Session key manuelle** : solution de secours si la connexion intégrée échoue (par exemple si Google refuse la connexion dans une fenêtre intégrée). Dans Chrome ou Edge sur claude.ai : `F12` → **Application** → **Cookies** → `https://claude.ai` → copie la valeur de `sessionKey`.
- **Effacer la session** : déconnecte Claude Bar (supprime ses cookies).

### Déploiement par l'IT

```
ClaudeBar-Setup.exe /S
```
Installation silencieuse pour l'utilisateur courant, avec démarrage automatique activé. Désinstallation silencieuse : `%LOCALAPPDATA%\Programs\ClaudeBar\Uninstall.exe /S`.

### Désinstallation

**Paramètres Windows** → **Applications** → **Claude Bar** → **Désinstaller**. Les réglages et la session sont supprimés aussi.

### Prérequis

- Windows 10 ou 11 (64 bits)
- **Microsoft Edge WebView2 Runtime** — déjà présent sur Windows 11 et sur les Windows 10 à jour. S'il manque, Claude Bar propose le lien de téléchargement.

---

## Comment ça marche

Même principe que la version macOS :

| macOS | Windows |
|---|---|
| `NSStatusItem` (texte dans la barre de menus) | `NotifyIcon` (pourcentage dessiné dans l'icône) |
| `NSPopover` + SwiftUI | Fenêtre sans bordure dessinée à la main (thème clair/sombre, DPI) |
| `WKWebView` caché + `callAsyncJavaScript` | `WebView2` cachée + `Runtime.evaluate` (DevTools Protocol, `awaitPromise`) |
| Cookies Safari partagés | Fenêtre de connexion intégrée (même profil WebView2) |
| `UserDefaults` | `%LOCALAPPDATA%\ClaudeBar\settings.json` |
| `SMAppService` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |

Flux : la WebView2 cachée charge `https://claude.ai` (passage Cloudflare), attend la fin du chargement, puis exécute dans la page `fetch('/api/organizations')` et `fetch('/api/organizations/{id}/usage')` → `five_hour.utilization`, `seven_day.utilization`, `five_hour.resets_at`.

Fichiers locaux, tous dans `%LOCALAPPDATA%\ClaudeBar\` :
- `settings.json` — espace de travail choisi
- `WebView2\` — profil navigateur dédié (cookies claude.ai, chiffrés par Windows)
- `claudebar.log` — journal de diagnostic (ne contient ni cookie ni clé)

---

## Développement

```
windows/
├── src/ClaudeBar/          Application WinForms (.NET 10)
│   ├── Program.cs              Point d'entrée, instance unique, --quit / --selftest
│   ├── TrayApplicationContext  Icône, menu, rafraîchissement (≈ AppDelegate)
│   ├── UsageService            WebView2 cachée + appels API
│   ├── ClaudeApi               fetch dans la page + parsing JSON
│   ├── UsagePopup              Fenêtre au clic (≈ PopoverView)
│   ├── SettingsForm            Paramètres (≈ SettingsView)
│   ├── LoginForm               Connexion claude.ai
│   └── TrayIconRenderer        Icône avec le pourcentage
├── installer/ClaudeBar.nsi  Installateur NSIS
├── build.ps1 / build.sh     Build complet → dist/ClaudeBar-Setup.exe
└── tools/make-icon.sh       Régénère app.ico (ImageMagick)
```

### Construire l'installateur

**Depuis macOS** (compilation croisée, pas besoin de Windows) :
```bash
brew install --cask dotnet-sdk   # .NET 10
brew install makensis
./windows/build.sh 1.0.0
```

**Depuis Windows** :
```powershell
winget install Microsoft.DotNet.SDK.10
winget install NSIS.NSIS
.\windows\build.ps1 -Version 1.0.0
```

Résultat : `windows/dist/ClaudeBar-Setup.exe` (≈ 33 Mo, runtime .NET inclus : rien d'autre à installer).

### Publier une version

Le workflow GitHub Actions `.github/workflows/windows.yml` compile sur un vrai Windows, lance l'autotest, puis publie l'installateur dans les Releases quand on pousse un tag :

```bash
git tag v1.0.0 && git push origin v1.0.0
```

Le lien de téléchargement `releases/latest/download/ClaudeBar-Setup.exe` pointe alors toujours vers la dernière version.

### Autotest

```
ClaudeBar.exe --selftest C:\temp\selftest
```
Génère des captures PNG de l'icône et de la fenêtre (tous les états, clair/sombre, 100 %/150 %), initialise WebView2 et interroge claude.ai. Résultat dans `selftest.txt` (code de sortie 0 = OK). Lancé automatiquement par la CI.

### Dépannage

- **Pourcentages à 0 %** : les clés de l'API interne ont peut-être changé — voir `ClaudeApi.ParseUsage`.
- **« Vérification Cloudflare »** : clique sur **Ouvrir la fenêtre claude.ai** pour valider le challenge, la fenêtre se referme ensuite.
- Consulte `%LOCALAPPDATA%\ClaudeBar\claudebar.log`.
