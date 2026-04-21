import SwiftUI
import AppKit

struct PopoverView: View {
    @ObservedObject var store: UsageStore
    var onRefresh: () -> Void = {}
    @State private var showSettings = false

    var body: some View {
        VStack(spacing: 0) {

            // ── En-tête ──────────────────────────────────────────────────
            HStack {
                Image(systemName: "sparkle")
                    .foregroundColor(.purple)
                    .font(.system(size: 14))
                Text("Claude Usage")
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
                Button {
                    onRefresh()
                } label: {
                    Image(systemName: store.isLoading ? "arrow.clockwise" : "arrow.clockwise")
                        .foregroundColor(.secondary)
                        .font(.system(size: 13))
                        .rotationEffect(.degrees(store.isLoading ? 360 : 0))
                        .animation(store.isLoading ? .linear(duration: 1).repeatForever(autoreverses: false) : .default, value: store.isLoading)
                }
                .buttonStyle(.plain)
                .disabled(store.isLoading)
                .help("Rafraîchir")

                Button {
                    showSettings.toggle()
                } label: {
                    Image(systemName: "gearshape")
                        .foregroundColor(.secondary)
                        .font(.system(size: 13))
                }
                .buttonStyle(.plain)
                .help("Entrer la session key manuellement")
            }
            .padding(.horizontal, 16)
            .padding(.top, 14)
            .padding(.bottom, 12)

            Divider()

            // ── Contenu ──────────────────────────────────────────────────
            if store.isLoading {
                HStack(spacing: 8) {
                    ProgressView().scaleEffect(0.7)
                    Text("Chargement…")
                        .font(.system(size: 12))
                        .foregroundColor(.secondary)
                }
                .frame(maxWidth: .infinity)
                .padding(24)

            } else if let err = store.errorMessage {
                VStack(spacing: 8) {
                    Image(systemName: "exclamationmark.triangle")
                        .font(.system(size: 20))
                        .foregroundColor(.orange)
                    Text(err)
                        .font(.system(size: 12))
                        .multilineTextAlignment(.center)
                        .foregroundColor(.secondary)
                    Button("Ouvrir claude.ai") {
                        NSWorkspace.shared.open(URL(string: "https://claude.ai")!)
                    }
                    .font(.system(size: 11))
                }
                .padding(16)

            } else {
                VStack(spacing: 16) {
                    UsageRowView(
                        label: "Session actuelle",
                        percent: store.sessionPercent
                    )
                    UsageRowView(
                        label: "Cette semaine",
                        percent: store.weeklyPercent
                    )
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 16)
            }

            Divider()

            // ── Pied de page ─────────────────────────────────────────────
            HStack {
                if let date = store.lastUpdated {
                    Text("Mis à jour \(timeAgo(date))")
                        .font(.system(size: 10))
                        .foregroundColor(.secondary)
                }
                Spacer()
                Button("Ouvrir Claude") {
                    NSWorkspace.shared.open(URL(string: "https://claude.ai")!)
                }
                .font(.system(size: 11))
                .buttonStyle(.plain)
                .foregroundColor(.accentColor)

                Button("Quitter") { NSApp.terminate(nil) }
                    .font(.system(size: 11))
                    .buttonStyle(.plain)
                    .foregroundColor(.secondary)
                    .padding(.leading, 8)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
        }
        .sheet(isPresented: $showSettings) {
            SettingsView()
        }
    }

    func timeAgo(_ date: Date) -> String {
        let mins = Int(Date().timeIntervalSince(date) / 60)
        if mins < 1 { return "à l'instant" }
        return "il y a \(mins) min"
    }
}

// ── Barre de progression ──────────────────────────────────────────────────────

struct UsageRowView: View {
    let label: String
    let percent: Int

    var color: Color {
        switch percent {
        case 0..<60: return Color(red: 0.2, green: 0.6, blue: 1.0)
        case 60..<85: return .orange
        default: return .red
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(label)
                    .font(.system(size: 12, weight: .medium))
                Spacer()
                Text("\(percent)%")
                    .font(.system(size: 13, weight: .semibold).monospacedDigit())
                    .foregroundColor(color)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 3)
                        .fill(Color.secondary.opacity(0.18))
                        .frame(height: 6)
                    RoundedRectangle(cornerRadius: 3)
                        .fill(color)
                        .frame(width: max(0, geo.size.width * CGFloat(percent) / 100), height: 6)
                        .animation(.easeOut(duration: 0.6), value: percent)
                }
            }
            .frame(height: 6)
        }
    }
}

// ── Paramètres (saisie manuelle) ─────────────────────────────────────────────

struct SettingsView: View {
    @AppStorage("claudeSessionKey") var sessionKey: String = ""
    @Environment(\.dismiss) var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Session Key manuelle")
                .font(.headline)

            Text("Si l'app ne détecte pas ton cookie automatiquement, colle ici la valeur du cookie `sessionKey` visible dans Safari > Développement > Afficher les cookies pour claude.ai")
                .font(.caption)
                .foregroundColor(.secondary)

            SecureField("Colle la valeur ici…", text: $sessionKey)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 11, design: .monospaced))

            HStack {
                Spacer()
                Button("Annuler") { dismiss() }
                Button("Enregistrer") {
                    UserDefaults.standard.set(sessionKey, forKey: "claudeSessionKey")
                    dismiss()
                }
                .buttonStyle(.borderedProminent)
            }
        }
        .padding(20)
        .frame(width: 380)
    }
}
