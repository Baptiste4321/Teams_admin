import Foundation
import CoreGraphics

/// Fenêtre capturable détectée (ScreenCaptureKit sur macOS)
public struct WindowInfo: Identifiable, Hashable {
    public var id: CGWindowID
    public var windowId: UInt32
    public var title: String
    public var processName: String
    public var bundleIdentifier: String
    public var frame: CGRect
    public var isTeams: Bool {
        processName.localizedCaseInsensitiveContains("Teams") ||
        title.localizedCaseInsensitiveContains("Teams") ||
        bundleIdentifier.localizedCaseInsensitiveContains("teams")
    }
    
    public init(windowId: UInt32, title: String, processName: String, bundleIdentifier: String = "", frame: CGRect = .zero) {
        self.id = CGWindowID(windowId)
        self.windowId = windowId
        self.title = title
        self.processName = processName
        self.bundleIdentifier = bundleIdentifier
        self.frame = frame
    }
}
