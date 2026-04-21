# ClaudeBar

A macOS menu bar app that shows your Claude.ai usage — current session and weekly limit.

![macOS](https://img.shields.io/badge/macOS-13%2B-black) ![Swift](https://img.shields.io/badge/Swift-5.9-orange)

---

## What it does

Sits quietly in your menu bar and displays:
- **Session usage** (last 5 hours)
- **Weekly usage**

Color changes from green → orange → red as you approach limits.

---

## Requirements

- macOS 13+
- Xcode 15+
- A Claude.ai account logged in via **Safari**

---

## Setup

**1. Clone and open in Xcode**
```bash
git clone https://github.com/yourname/claudeBar.git
```
Open `claudeBar.xcodeproj`.

**2. Disable App Sandbox** (required for network access)

Xcode → target `claudeBar` → Signing & Capabilities → click **–** on App Sandbox

**3. Run**
```
⌘R
```
The icon appears in your menu bar.

---

## Usage

The app reads your session cookie automatically from Safari's shared WebKit storage.

**Safari users** — just make sure you're logged into claude.ai in Safari. That's it.

**Chrome users** — click the gear icon ⚙️ in the popover and paste your `sessionKey` cookie manually:
> Chrome → DevTools (F12) → Application → Cookies → claude.ai → `sessionKey`

---

## Install as a standalone app

```bash
cp -R ~/Library/Developer/Xcode/DerivedData/claudeBar-*/Build/Products/Debug/claudeBar.app /Applications/
```

To launch at login: System Settings → General → Login Items → add `claudeBar.app`

---

## Troubleshooting

**Percentages stuck at 0%** — Claude's internal API may have changed. Check Network tab in Safari Web Inspector on claude.ai and update the keys in `AppDelegate.swift`.

**Popover doesn't open** — Make sure App Sandbox is disabled.
