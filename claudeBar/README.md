# ClaudeBar — Setup Xcode (5 minutes)

## 1. Créer le projet Xcode

1. Ouvre **Xcode** → File → New → Project
2. Choisis **macOS → App**
3. Remplis :
   - Product Name : `ClaudeBar`
   - Interface : **SwiftUI**
   - Language : **Swift**
   - ✅ Décocher "Include Tests"
4. Clique **Next** → choisis un dossier → **Create**

---

## 2. Remplacer les fichiers

Dans le Finder, **supprime tous les fichiers .swift** créés par Xcode, puis **glisse-dépose** les 4 fichiers de ce dossier dans le projet Xcode :

- `ClaudeBarApp.swift`
- `AppDelegate.swift`
- `UsageStore.swift`
- `PopoverView.swift`

> Xcode te demande "Copy items if needed?" → coche la case → Add

---

## 3. Désactiver le Sandbox (IMPORTANT)

Sans ça, l'app ne peut pas accéder à Internet ni aux cookies.

1. Dans le panneau gauche de Xcode, clique sur **ClaudeBar** (le fichier bleu tout en haut)
2. Sélectionne la target **ClaudeBar**
3. Onglet **Signing & Capabilities**
4. Clique sur **App Sandbox** → clique le **–** pour le supprimer
5. Confirme la suppression

---

## 4. Lancer

**⌘ + R** — l'app apparaît dans ta barre de menu en haut à droite.

---

## Comment ça marche (automatique)

L'app lit les cookies de claude.ai automatiquement via le WebKit partagé avec Safari. Pour que ça fonctionne :

1. Ouvre **Safari** et connecte-toi à https://claude.ai
2. Relance l'app — elle détecte la session automatiquement ✅

Si tu utilises Chrome, va dans **Réglages ⚙️** dans le popover et colle manuellement le cookie :
- Chrome → DevTools (F12) → Application → Cookies → claude.ai → `sessionKey`

---

## Démarrage automatique au login

1. Xcode → Product → **Archive**
2. Distribue → Custom → Copy App
3. Copie l'app dans `/Applications`
4. Préférences Système → Général → **Éléments de connexion** → ajoute ClaudeBar.app

---

## Adapter si les données ne s'affichent pas

L'API interne de claude.ai peut changer. Si le pourcentage reste à 0% :

1. Ouvre Safari → claude.ai → Développement → Afficher les ressources web
2. Onglet Réseau → filtre sur `/api/`
3. Trouve la requête qui retourne les limites
4. Mets à jour les clés dans `AppDelegate.swift` → `fetchLimits()`
