import Foundation

/// Modèle complet d'une session de réunion (miroir de MeetingSession C#)
public class MeetingSession: Identifiable, ObservableObject, Equatable {
    public let id: String
    @Published public var title: String
    @Published public var startTime: Date
    @Published public var endTime: Date?
    @Published public var rootDirectory: URL
    
    @Published public var metadata: MeetingMetadata
    @Published public var transcriptSegments: [TranscriptSegment] = []
    @Published public var chatMessages: [ChatMessage] = []
    @Published public var aiAnalysis: AiAnalysisResult?
    
    public var duration: TimeInterval {
        if let end = endTime {
            return end.timeIntervalSince(startTime)
        }
        return Date().timeIntervalSince(startTime)
    }
    
    // Sous-répertoires
    public var videoDirectory: URL { rootDirectory.appendingPathComponent("video") }
    public var audioDirectory: URL { rootDirectory.appendingPathComponent("audio") }
    public var transcriptDirectory: URL { rootDirectory.appendingPathComponent("transcript") }
    public var chatDirectory: URL { rootDirectory.appendingPathComponent("chat") }
    public var metadataDirectory: URL { rootDirectory.appendingPathComponent("metadata") }
    public var logsDirectory: URL { rootDirectory.appendingPathComponent("logs") }
    
    // Fichiers cibles
    public var videoFilePath: URL { videoDirectory.appendingPathComponent("recording.mp4") }
    public var systemAudioFilePath: URL { audioDirectory.appendingPathComponent("system_audio.wav") }
    public var microphoneFilePath: URL { audioDirectory.appendingPathComponent("microphone.wav") }
    public var meetingJsonPath: URL { metadataDirectory.appendingPathComponent("meeting.json") }
    public var transcriptJsonPath: URL { transcriptDirectory.appendingPathComponent("transcript.json") }
    public var transcriptTxtPath: URL { transcriptDirectory.appendingPathComponent("transcript.txt") }
    public var chatJsonPath: URL { chatDirectory.appendingPathComponent("chat.json") }
    
    // Détection dynamique des médias existants
    public var existingVideoURL: URL? {
        let fm = FileManager.default
        let mp4 = videoFilePath
        if fm.fileExists(atPath: mp4.path) { return mp4 }
        let avi = videoDirectory.appendingPathComponent("recording.avi")
        if fm.fileExists(atPath: avi.path) { return avi }
        if let files = try? fm.contentsOfDirectory(at: videoDirectory, includingPropertiesForKeys: nil) {
            return files.first { $0.pathExtension.lowercased() == "mp4" || $0.pathExtension.lowercased() == "mov" || $0.pathExtension.lowercased() == "avi" }
        }
        return nil
    }
    
    public var existingAudioURL: URL? {
        let fm = FileManager.default
        let sys = systemAudioFilePath
        if fm.fileExists(atPath: sys.path) { return sys }
        let mic = microphoneFilePath
        if fm.fileExists(atPath: mic.path) { return mic }
        if let files = try? fm.contentsOfDirectory(at: audioDirectory, includingPropertiesForKeys: nil) {
            return files.first { $0.pathExtension.lowercased() == "wav" || $0.pathExtension.lowercased() == "m4a" || $0.pathExtension.lowercased() == "aac" }
        }
        return nil
    }
    
    public var hasMedia: Bool { existingVideoURL != nil || existingAudioURL != nil }
    
    public init(id: String, title: String, startTime: Date = Date(), rootDirectory: URL, metadata: MeetingMetadata) {
        self.id = id
        self.title = title
        self.startTime = startTime
        self.rootDirectory = rootDirectory
        self.metadata = metadata
    }
    
    public static func == (lhs: MeetingSession, rhs: MeetingSession) -> Bool {
        lhs.id == rhs.id
    }
}
