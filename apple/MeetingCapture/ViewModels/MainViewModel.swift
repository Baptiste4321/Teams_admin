import Foundation
import Combine

/// ViewModel principal pour l'interface de démarrage et contrôle REC
public class MainViewModel: ObservableObject {
    @Published public var meetingTitle: String = ""
    @Published public var selectedWindow: WindowInfo?
    @Published public var isAlwaysOnTop: Bool = false
    @Published public var isDetailsExpanded: Bool = false
    
    // Navigation / UI State
    @Published public var showHistory: Bool = false
    @Published public var showSettings: Bool = false
    @Published public var pulseRec: Bool = false
    @Published public var selectedTab: Int = 0
    
    // Services
    @Published public var orchestrator = MeetingOrchestrator.shared
    @Published public var captureService = ScreenCaptureService.shared
    @Published public var audioService = AudioCaptureService.shared
    
    public var isRecording: Bool { orchestrator.isRecording }
    public var statusMessage: String { orchestrator.statusMessage }
    
    public var elapsedTimeText: String {
        let total = Int(orchestrator.elapsedTime)
        let hours = total / 3600
        let minutes = (total % 3600) / 60
        let seconds = total % 60
        if hours > 0 {
            return String(format: "%02d:%02d:%02d", hours, minutes, seconds)
        }
        return String(format: "%02d:%02d", minutes, seconds)
    }
    
    public init() {
        Task {
            await refreshWindows()
        }
    }
    
    public func refreshWindows() async {
        await captureService.refreshWindows()
        if selectedWindow == nil {
            // Sélectionner Teams automatiquement si présent
            selectedWindow = captureService.availableWindows.first { $0.isTeams } ?? captureService.availableWindows.first
        }
    }
    
    public func toggleRecord() async {
        if isRecording {
            _ = await orchestrator.stopRecording()
        } else {
            let title = meetingTitle.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : meetingTitle
            do {
                try await orchestrator.startRecording(meetingTitle: title, targetWindow: selectedWindow)
            } catch {
                orchestrator.statusMessage = "Erreur : \(error.localizedDescription)"
            }
        }
    }
}
