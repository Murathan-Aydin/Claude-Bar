# Publier Claude Bar sur le Microsoft Store

Une app publiée sur le Store est **signée par Microsoft** : aucun avertissement SmartScreen ni antivirus, installation en un clic depuis le Store. Le compte développeur individuel est **gratuit**.

Le paquet `.msix` est construit automatiquement par la CI (job `msix` du workflow *Windows*). Il ne reste que les étapes ci-dessous, qui doivent être faites par le titulaire du compte.

---

## 1. Créer le compte développeur (une seule fois, ~15 min)

1. Va sur **https://storedeveloper.microsoft.com** et connecte-toi avec ton compte Microsoft personnel.
2. Choisis **Individuel**, renseigne ton nom (il apparaîtra comme éditeur) et passe la vérification d'identité (pièce d'identité + selfie).
3. La validation du compte peut prendre de quelques heures à quelques jours.

## 2. Réserver le nom de l'application

Partner Center → **Applications et jeux** → **Nouveau produit** → **Application MSIX ou PWA** → saisis le nom.

> ⚠️ **Nom et marque « Claude »** : « Claude » est une marque d'Anthropic. La certification peut refuser un nom qui laisse croire à une app officielle. Si « Claude Bar » est refusé, essaie un nom descriptif du type **« Usage Bar for Claude »** et garde la mention « non officiel » dans la description (déjà prévue ci-dessous).

## 3. Reporter l'identité du paquet dans le dépôt

Partner Center → ton app → **Gestion des produits** → **Identité du produit**. Copie les trois valeurs dans `windows/store/identity.json` :

| Partner Center | `identity.json` |
|---|---|
| `Package/Identity/Name` (ex. `12345MurathanAydin.ClaudeBar`) | `identityName` |
| `Package/Identity/Publisher` (ex. `CN=1A2B3C4D-…`) | `publisher` |
| `Package/Properties/PublisherDisplayName` | `publisherDisplayName` |
| Nom réservé à l'étape 2 | `displayName` |

Puis commite, pousse et crée une version :
```bash
git tag v1.1.0 && git push origin main v1.1.0
```
Dans l'onglet **Actions** de GitHub, ouvre l'exécution du tag → artefact **ClaudeBar-Store** → `ClaudeBar-Store.msix`.

> Le paquet est volontairement **non signé** : le Store le signe lui-même. Chaque nouvelle soumission doit avoir un numéro de version plus élevé (nouveau tag).

## 4. Remplir la soumission

Partner Center → ton app → **Démarrer la soumission**.

### Tarification et disponibilité
- Prix : **Gratuit**
- Marchés : tous (ou France uniquement)
- Visibilité : publique (ou « Masquée, accessible par lien direct » pour une diffusion interne)

### Propriétés
- Catégorie : **Productivité** (sous-catégorie libre)
- Politique de confidentialité : `https://github.com/Murathan-Aydin/Claude-Bar/blob/main/PRIVACY.md`
- Site web : `https://github.com/Murathan-Aydin/Claude-Bar`
- Configuration système : rien de particulier (Windows 10 1809+ 64 bits)

### Classification par âge
Remplis le questionnaire IARC : pas de contenu violent/sexuel, pas d'achat, **l'app accède à Internet** et **ne permet pas d'échanger avec d'autres utilisateurs**. Résultat attendu : 3+ / PEGI 3.

### Packages
Téléverse `ClaudeBar-Store.msix`. Famille d'appareils : **Desktop** uniquement.

### Fiche du Store (langue : français)

**Description**
```
Claude Bar affiche en permanence ton utilisation de Claude.ai dans la zone de notification de Windows, près de l'horloge.

• Pourcentage de la session en cours (fenêtre de 5 heures), directement dans l'icône
• Limite hebdomadaire et heure de réinitialisation de la session
• Couleur verte, orange puis rouge à l'approche des limites
• Plusieurs espaces de travail (compte personnel, équipe…)
• Thème clair / sombre, lancement au démarrage de Windows

Connecte-toi une fois à claude.ai dans la fenêtre intégrée : Claude Bar rafraîchit ensuite les chiffres toutes les 5 minutes.

Application open source et non officielle, sans lien avec Anthropic. Aucune donnée n'est collectée : l'application communique uniquement avec claude.ai, avec ton propre compte.
```

**Description courte** : `Ton utilisation de Claude.ai en un coup d'œil, dans la barre des tâches.`

**Fonctionnalités** (une par ligne) : `Pourcentage de session dans l'icône` · `Limite hebdomadaire` · `Heure de réinitialisation` · `Plusieurs espaces de travail` · `Thème clair et sombre` · `Lancement au démarrage`

**Mots-clés** : `Claude`, `IA`, `utilisation`, `quota`, `limite`, `barre des tâches`, `Anthropic`

**Captures d'écran** : les fichiers de `windows/store/screenshots/` (1920×1080).

**Logos de la fiche** : facultatifs, le Store utilise les icônes du paquet par défaut.

### Justification de la capacité restreinte `runFullTrust`
Partner Center la demande pour les applications de bureau :
```
Claude Bar est une application de bureau Win32 (.NET / WinForms) empaquetée en MSIX. runFullTrust est nécessaire pour afficher une icône dans la zone de notification (NotifyIcon) et héberger le contrôle Microsoft Edge WebView2 utilisé pour se connecter à claude.ai.
```

### Notes pour la certification
```
L'application affiche l'utilisation d'un compte Claude.ai (service tiers). Sans compte, elle s'ouvre sur l'écran « Connecte-toi à claude.ai » : le bouton « Se connecter » ouvre la page de connexion officielle de claude.ai dans une fenêtre WebView2. L'icône apparaît dans la zone de notification (près de l'horloge, éventuellement derrière la flèche ^). Clic gauche : fenêtre d'utilisation ; clic droit : menu.
```

## 5. Envoyer et attendre la certification

Clique sur **Soumettre au Store**. La certification prend généralement **quelques heures à 3 jours ouvrés**. En cas de refus, Partner Center indique la règle concernée (souvent le nom ou les captures).

## 6. Après publication

- Le lien public a la forme `https://apps.microsoft.com/detail/<ID-du-Store>` : ajoute-le au README.
- Les utilisateurs peuvent aussi installer en ligne de commande : `winget install <ID-du-Store>`.
- Mise à jour : nouveau tag `vX.Y.Z` → télécharge le nouveau `ClaudeBar-Store.msix` → **nouvelle soumission** avec ce paquet. Le Store met à jour les postes automatiquement.
