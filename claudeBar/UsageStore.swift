import Foundation
import Combine

class UsageStore: ObservableObject {
    @Published var sessionPercent: Int  = 0
    @Published var weeklyPercent: Int   = 0
    @Published var isLoading: Bool      = true
    @Published var errorMessage: String? = nil
    @Published var lastUpdated: Date?   = nil
    @Published var sessionResetAt: Date? = nil
    @Published var organizations: [Organization] = []
    @Published var currentOrgName: String? = nil
}

struct Organization: Identifiable, Hashable {
    let id: String
    let name: String
}
