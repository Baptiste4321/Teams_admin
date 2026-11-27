import Foundation

/// Service de stockage local macOS et iOS avec versioning automatique _v1, _v2
public class StorageService {
    public static let shared = StorageService()
    
    public var defaultStorageRoot: URL {
        let fm = FileManager.default
        let docs = fm.urls(for: .documentDirectory, in: .userDomainMask).first!
        return docs.appendingPathComponent("MeetingCapture")
    }
    
    public init() {}
    
    /// Crée une nouvelle session de réunion avec garantie d'unicité (_v1, _v2 si collision)
    public func createMeetingSession(title: String? = nil, customBasePath: URL? = nil) -> MeetingSession {
        let now = Date()
        let cal = Calendar.current
        let year = String(cal.component(.year, from: now))
        let month = String(format: "%02d", cal.component(.month, from: now))
        let day = String(format: "%02d", cal.component(.day, from: now))
        let timeStr = String(format: "%02d-%02d", cal.component(.hour, from: now), cal.component(.minute, from: now))
        
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd_HHmmss"
        let sessionId = "meeting_\(formatter.string(from: now))_\(UUID().uuidString.prefix(6))"
        
        let rawTitle = (title?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false) ? title! : "Meeting_\(timeStr)"
        let safeTitle = sanitizeFileName(rawTitle)
        
        let root = customBasePath ?? defaultStorageRoot
        let dayDir = root.appendingPathComponent(year).appendingPathComponent(month).appendingPathComponent(day)
        var sessionDir = dayDir.appendingPathComponent(safeTitle)
        var finalTitle = (title?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false) ? title! : "Réunion du \(now.formatted(date: .numeric, time: .shortened))"
        
        let fm = FileManager.default
        // Gestion de collision automatique : _v1, _v2, _v3...
        if fm.fileExists(atPath: sessionDir.path) {
            var version = 1
            var candidateDir: URL
            repeat {
                candidateDir = dayDir.appendingPathComponent("\(safeTitle)_v\(version)")
                version += 1
            } while fm.fileExists(atPath: candidateDir.path)
            
            sessionDir = candidateDir
            finalTitle = "\(finalTitle) (v\(version - 1))"
        }
        
        formatter.dateFormat = "yyyy-MM-dd"
        let metadata = MeetingMetadata(
            id: sessionId,
            name: sessionDir.lastPathComponent,
            date: formatter.string(from: now),
            startTimeUtc: now,
            platform: "Apple (macOS/iOS)"
        )
        
        let session = MeetingSession(
            id: sessionId,
            title: finalTitle,
            startTime: now,
            rootDirectory: sessionDir,
            metadata: metadata
        )
        
        // Création des sous-répertoires
        try? fm.createDirectory(at: session.videoDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: session.audioDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: session.transcriptDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: session.chatDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: session.metadataDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: session.logsDirectory, withIntermediateDirectories: true)
        
        return session
    }
    
    /// Sauvegarde les métadonnées meeting.json
    public func saveMetadata(session: MeetingSession) async {
        session.metadata.endTimeUtc = session.endTime ?? Date()
        session.metadata.durationSeconds = session.duration
        
        let encoder = JSONEncoder()
        encoder.outputFormatting = .prettyPrinted
        encoder.dateEncodingStrategy = .iso8601
        
        if let data = try? encoder.encode(session.metadata) {
            try? data.write(to: session.meetingJsonPath)
        }
    }
    
    /// Sauvegarde la transcription en JSON et TXT
    public func saveTranscript(session: MeetingSession) async {
        let encoder = JSONEncoder()
        encoder.outputFormatting = .prettyPrinted
        
        if let data = try? encoder.encode(session.transcriptSegments) {
            try? data.write(to: session.transcriptJsonPath)
        }
        
        let plainText = session.transcriptSegments.map { "[\($0.speaker)] \($0.text)" }.joined(separator: "\n")
        try? plainText.write(to: session.transcriptTxtPath, atomically: true, encoding: .utf8)
    }
    
    /// Exporte une session complète en archive ZIP
    public func exportMeetingZip(session: MeetingSession, destinationURL: URL) async -> Bool {
        let fm = FileManager.default
        let coordinator = NSFileCoordinator()
        var success = false
        
        coordinator.coordinate(readingItemAt: session.rootDirectory, options: .forUploading, error: nil) { zipTempURL in
            do {
                if fm.fileExists(atPath: destinationURL.path) {
                    try fm.removeItem(at: destinationURL)
                }
                try fm.copyItem(at: zipTempURL, to: destinationURL)
                success = true
            } catch {
                success = false
            }
        }
        return success
    }
    
    /// Charge l'historique complet des réunions
    public func loadMeetings() async -> [MeetingSession] {
        let fm = FileManager.default
        var result: [MeetingSession] = []
        guard fm.fileExists(atPath: defaultStorageRoot.path) else { return [] }
        
        let enumerator = fm.enumerator(at: defaultStorageRoot, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles])
        
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        
        while let fileURL = enumerator?.nextObject() as? URL {
            if fileURL.lastPathComponent == "meeting.json" {
                if let data = try? Data(contentsOf: fileURL),
                   let metadata = try? decoder.decode(MeetingMetadata.self, from: data) {
                    let sessionDir = fileURL.deletingLastPathComponent().deletingLastPathComponent()
                    let session = MeetingSession(
                        id: metadata.id,
                        title: metadata.name,
                        startTime: metadata.startTimeUtc,
                        rootDirectory: sessionDir,
                        metadata: metadata
                    )
                    session.endTime = metadata.endTimeUtc
                    
                    // Charger transcription si existante
                    if let transData = try? Data(contentsOf: session.transcriptJsonPath),
                       let segments = try? JSONDecoder().decode([TranscriptSegment].self, from: transData) {
                        session.transcriptSegments = segments
                    }
                    
                    result.append(session)
                }
            }
        }
        
        return result.sorted { $0.startTime > $1.startTime }
    }
    
    /// Supprime définitivement une session
    public func deleteMeeting(session: MeetingSession) async -> Bool {
        do {
            try FileManager.default.removeItem(at: session.rootDirectory)
            return true
        } catch {
            return false
        }
    }
    
    private func sanitizeFileName(_ name: String) -> String {
        let invalid = CharacterSet(charactersIn: "/\\?%*|\"<>:")
        return name.components(separatedBy: invalid).joined(separator: "_")
    }
}
