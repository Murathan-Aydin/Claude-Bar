import AppKit
import SwiftUI
import WebKit

class AppDelegate: NSObject, NSApplicationDelegate {
    var statusItem: NSStatusItem?
    var popover: NSPopover?
    var timer: Timer?
    let store = UsageStore()
    var hiddenWebView: WKWebView?
    var eventMonitor: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem?.button {
            button.title = "◆ …"
            button.font = .monospacedDigitSystemFont(ofSize: 12, weight: .medium)
            button.action = #selector(togglePopover)
            button.target = self
        }

        let view = PopoverView(store: store, onRefresh: { [weak self] in self?.fetchUsage() })
        let controller = NSHostingController(rootView: view)
        popover = NSPopover()
        popover?.contentViewController = controller
        popover?.behavior = .applicationDefined
        popover?.contentSize = NSSize(width: 300, height: 240)

        // Create a persistent hidden WKWebView for API calls (bypasses Cloudflare)
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        hiddenWebView = WKWebView(frame: .zero, configuration: config)

        // First fetch + timer every 5 minutes
        fetchUsage()
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.fetchUsage()
        }
    }

    @objc func togglePopover() {
        guard let button = statusItem?.button else { return }
        if popover?.isShown == true {
            closePopover()
        } else {
            NSApp.activate(ignoringOtherApps: true)
            popover?.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
            eventMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
                self?.closePopover()
            }
        }
    }

    func closePopover() {
        popover?.performClose(nil)
        if let monitor = eventMonitor {
            NSEvent.removeMonitor(monitor)
            eventMonitor = nil
        }
    }

    // MARK: - Fetch usage via hidden WKWebView (bypasses Cloudflare)

    func fetchUsage() {
        // Only show spinner on first load (no cached data yet)
        if store.lastUpdated == nil {
            store.isLoading = true
        }

        // Inject the manual session key cookie if set
        let savedKey = UserDefaults.standard.string(forKey: "claudeSessionKey") ?? ""
        if !savedKey.isEmpty {
            let cookieProps: [HTTPCookiePropertyKey: Any] = [
                .name: "sessionKey",
                .value: savedKey,
                .domain: ".claude.ai",
                .path: "/",
                .secure: true
            ]
            if let cookie = HTTPCookie(properties: cookieProps) {
                hiddenWebView?.configuration.websiteDataStore.httpCookieStore.setCookie(cookie) { [weak self] in
                    print("[claudeBar] Injected manual sessionKey cookie")
                    self?.loadAndFetch()
                }
                return
            }
        }

        loadAndFetch()
    }

    func loadAndFetch() {
        guard let webView = hiddenWebView else { return }

        // Skip full page reload if claude.ai is already loaded (Cloudflare clearance intact)
        if let current = webView.url, current.host == "claude.ai", !webView.isLoading {
            fetchOrganizationsViaJS()
            return
        }

        let request = URLRequest(url: URL(string: "https://claude.ai")!)
        webView.load(request)

        DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) { [weak self] in
            self?.fetchOrganizationsViaJS()
        }
    }

    func fetchOrganizationsViaJS() {
        let js = """
        const r = await fetch('/api/organizations', {
            credentials: 'include',
            headers: { 'Accept': 'application/json' }
        });
        const t = await r.text();
        return t;
        """

        hiddenWebView?.callAsyncJavaScript(js, arguments: [:], in: nil, in: .page) { [weak self] result in
            let (result, error): (Any?, Error?) = {
                switch result {
                case .success(let val): return (val, nil)
                case .failure(let err): return (nil, err)
                }
            }()
            if let error = error {
                print("[claudeBar] JS /organizations error: \(error)")
                DispatchQueue.main.async {
                    self?.store.isLoading = false
                    self?.store.errorMessage = "Erreur JS: \(error.localizedDescription)"
                }
                return
            }

            guard let text = result as? String,
                  let data = text.data(using: .utf8),
                  let json = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]],
                  let orgId = json.first?["uuid"] as? String else {
                print("[claudeBar] /organizations parse failed, result: \(String(describing: result))")
                DispatchQueue.main.async {
                    self?.store.isLoading = false
                    self?.store.errorMessage = "Connecte-toi à claude.ai via le bouton ci-dessous"
                }
                return
            }

            print("[claudeBar] Found orgId: \(orgId)")
            self?.fetchUsageViaJS(orgId: orgId)
        }
    }

    func fetchUsageViaJS(orgId: String) {
        let js = """
        const r = await fetch('/api/organizations/\(orgId)/usage', {
            credentials: 'include',
            headers: { 'Accept': 'application/json' }
        });
        const t = await r.text();
        return t;
        """

        hiddenWebView?.callAsyncJavaScript(js, arguments: [:], in: nil, in: .page) { [weak self] result in
            let (result, error): (Any?, Error?) = {
                switch result {
                case .success(let val): return (val, nil)
                case .failure(let err): return (nil, err)
                }
            }()
            if let error = error {
                print("[claudeBar] JS /usage error: \(error)")
                return
            }

            guard let text = result as? String,
                  let data = text.data(using: .utf8) else {
                print("[claudeBar] /usage no text result")
                return
            }

            print("[claudeBar] /usage response: \(text.prefix(500))")

            var session = 0
            var weekly = 0

            if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                if let fiveHour = json["five_hour"] as? [String: Any],
                   let util = fiveHour["utilization"] as? Double {
                    session = min(100, Int(util))
                }
                if let sevenDay = json["seven_day"] as? [String: Any],
                   let util = sevenDay["utilization"] as? Double {
                    weekly = min(100, Int(util))
                }
            }

            DispatchQueue.main.async { [weak self] in
                self?.store.sessionPercent = session
                self?.store.weeklyPercent  = weekly
                self?.store.lastUpdated    = Date()
                self?.store.isLoading      = false
                self?.store.errorMessage   = nil
                self?.updateMenuBar(session: session, weekly: weekly)
            }
        }
    }

    // MARK: - Menu bar icon

    func updateMenuBar(session: Int?, weekly: Int?) {
        guard let button = statusItem?.button else { return }
        if let s = session {
            let icon = s >= 90 ? "🔴" : s >= 60 ? "🟡" : "🟢"
            button.title = "\(icon) \(s)%"
        } else {
            button.title = "◆ –"
        }
    }
}
