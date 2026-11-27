import Foundation
import Combine

/// Orchestrateur central de réunion (miroir de MeetingOrchestrator C#)
public class MeetingOrchestrator: ObservableObject {
    public static let shared = MeetingOrchestrator()
    
    @Published public var currentSession: MeetingSession?
    @Published public var isRecording: Bool = false
    @Published public var elapsedTime: TimeInterval = 0.0
    @Published public var statusMessage: String = "Prêt à enregistrer"
    
    private var timer: AnyCancellable?
    private let storage = StorageService.shared
    private let capture = ScreenCaptureService.shared
    private let audio = AudioCaptureService.shared
    private let transcription = TranscriptionService.shared
    
    public init() {}
    
    /// Démarre une session d'enregistrement avec isolation de fenêtre (Teams)
    @MainActor
    public func startRecording(meetingTitle: String?, targetWindow: WindowInfo?, fps: Int = 30) async throws {
        // 1. Création de la session avec nommage unique garanti (_v1, _v2 si collision)
        let session = storage.createMeetingSession(title: meetingTitle)
        self.currentSession = session
        
        statusMessage = "Démarrage des flux de capture..."
        
        // 2. Démarrage de la capture vidéo et audio isolé
        try await capture.startCapture(targetWindow: targetWindow, outputVideoURL: session.videoFilePath, fps: fps)
        audio.startMonitoring()
        
        // 3. Démarrage du chronomètre
        self.isRecording = true
        self.elapsedTime = 0.0
        self.statusMessage = "Enregistrement en cours..."
        
        timer = Timer.publish(every: 1.0, on: .main, in: .common)
            .autoconnect()
            .sink { [weak self] _ in
                guard let self = self, let s = self.currentSession else { return }
                self.elapsedTime = s.duration
            }
    }
    
    /// Arrête la session, sauvegarde et lance la transcription
    @MainActor
    public func stopRecording() async -> MeetingSession? {
        guard let session = currentSession else { return nil }
        
        statusMessage = "Finalisation des fichiers..."
        timer?.cancel()
        timer = nil
        
        // 1. Arrêt des flux
        await capture.stopCapture()
        audio.stopMonitoring()
        
        session.endTime = Date()
        self.isRecording = false
        
        // 2. Sauvegarde des métadonnées
        await storage.saveMetadata(session: session)
        
        // 3. Transcription automatique en post-traitement
        statusMessage = "Transcription Whisper en cours..."
        if let audioURL = session.existingAudioURL {
            let segments = await transcription.transcribeAudioFile(audioURL: audioURL, language: "fr")
            session.transcriptSegments = segments
            await storage.saveTranscript(session: session)
        }
        
        statusMessage = "Enregistrement terminé et archivé."
        self.currentSession = nil
        return session
    }
}
