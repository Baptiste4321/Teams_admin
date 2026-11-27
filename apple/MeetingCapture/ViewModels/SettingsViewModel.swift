import Foundation

/// ViewModel pour la configuration des paramètres vidéo et audio
public class SettingsViewModel: ObservableObject {
    @Published public var fps: Int = 30
    @Published public var isMicEnabled: Bool = false
    @Published public var isDesktopMode: Bool = false
    @Published public var defaultMeetingTitle: String = ""
    
    public let fpsOptions: [Int] = [15, 30, 60]
    
    public init() {}
}
