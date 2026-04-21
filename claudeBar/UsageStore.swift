import Foundation
import Combine

class UsageStore: ObservableObject {
    @Published var sessionPercent: Int  = 0
    @Published var weeklyPercent: Int   = 0
    @Published var isLoading: Bool      = true
    @Published var errorMessage: String? = nil
    @Published var lastUpdated: Date?   = nil
}
