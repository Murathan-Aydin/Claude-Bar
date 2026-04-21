# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

Open in Xcode, press **⌘R**. No CLI build system — Xcode only.

**Required Xcode setup (one-time):**
- Target → Signing & Capabilities → remove **App Sandbox** (required for network + cookie access)
- Deployment target: macOS

## Architecture

macOS menu bar app. No dock icon (`NSApp.setActivationPolicy(.accessory)`).

**Entry point:** `ClaudeBarApp.swift` — `@main` SwiftUI `App`, wires `AppDelegate` via `@NSApplicationDelegateAdaptor`. The `Settings { EmptyView() }` scene is a no-op required to suppress the default window.

**`AppDelegate`** owns everything:
- `NSStatusItem` — menu bar button showing usage %
- `NSPopover` (`.transient` behavior) — shown on button click
- `WKWebView` (hidden, persistent) — used for all API calls
- `UsageStore` — shared state passed into `PopoverView`
- `Timer` — refreshes every 5 minutes

**`UsageStore`** — simple `ObservableObject` with `@Published` properties: `sessionPercent`, `weeklyPercent`, `isLoading`, `errorMessage`, `lastUpdated`.

**`PopoverView`** — SwiftUI view observing `UsageStore`. Contains `UsageRowView` (progress bar row) and `SettingsView` (sheet for manual session key).

## Data Fetch Flow

The hidden `WKWebView` loads `claude.ai` first to acquire Cloudflare clearance cookies, then calls internal APIs via `callAsyncJavaScript`:

1. `fetchUsage()` — inject `sessionKey` cookie if manually set, then call `loadAndFetch()`
2. `loadAndFetch()` — load `https://claude.ai`, then **hardcoded 4s delay** before step 3
3. `fetchOrganizationsViaJS()` — JS `fetch('/api/organizations')` → extract `uuid` (org ID)
4. `fetchUsageViaJS(orgId:)` — JS `fetch('/api/organizations/{orgId}/usage')` → parse `five_hour.utilization` and `seven_day.utilization` as `Double`, capped at 100

**Known issue — popover not showing reliably:** `togglePopover()` calls `popover?.show(...)` then `NSApp.activate(ignoringOtherApps: true)`. The activation sometimes steals focus before the popover renders. Fix: activate before showing, or use `NSPopover` with an `NSEvent` monitor for outside-click dismissal instead of `.transient`.

**Known issue — sluggish feel:** The 4s `asyncAfter` in `loadAndFetch()` is a fixed wait for Cloudflare page load. During this time `isLoading = true` but the popover may already be visible. Consider showing cached data immediately on open and refreshing in background.

## Session Key

Stored in `UserDefaults` under key `"claudeSessionKey"`. If set, it is injected as an `HTTPCookie` into the `WKWebView`'s cookie store before each fetch. Safari users get cookies automatically via shared WebKit storage; Chrome users must paste manually via the gear icon in the popover.

## API Shape (internal claude.ai)

```
GET /api/organizations              → [{ uuid, ... }]
GET /api/organizations/{id}/usage   → { five_hour: { utilization: Double }, seven_day: { utilization: Double } }
```

If percentages stay at 0%, the API keys (`five_hour`, `seven_day`, `utilization`) may have changed — inspect Network tab in Safari Web Inspector on claude.ai.
