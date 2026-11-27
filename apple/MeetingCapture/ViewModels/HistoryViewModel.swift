import Foundation
import AVFoundation
import Combine
#if os(macOS)
import AppKit
#elseif os(iOS)
import UIKit
#endif

/// ViewModel pour la relecture synchronisée, l'historique et l'export ZIP
public class HistoryViewModel: ObservableObject {
    @Published public var meetings: [MeetingSession] = []
    @Published public var filteredMeetings: [MeetingSession] = []
    @Published public var selectedMeeting: MeetingSession?
    @Published public var filterText: String = "" {
        didSet { applyFilter() }
    }
    
    // État du lecteur multimédia
    @Published public var isPlaying: Bool = false
    @Published public var currentTime: Double = 0.0
    @Published public var totalDuration: Double = 0.0
    @Published public var activeSegment: TranscriptSegment?
    @Published public var isTranscribing: Bool = false
    @Published public var exportMessage: String?
    
    public var player: AVPlayer?
    private var timeObserver: Any?
    private let storage = StorageService.shared
    
    public init() {
        Task {
            await loadMeetings()
        }
    }
    
    public func loadMeetings() async {
        let loaded = await storage.loadMeetings()
        await MainActor.run {
            self.meetings = loaded
            self.applyFilter()
            if self.selectedMeeting == nil, let first = self.filteredMeetings.first {
                self.selectMeeting(first)
            }
        }
    }
    
    public func applyFilter() {
        if filterText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            filteredMeetings = meetings
        } else {
            let term = filterText.lowercased()
            filteredMeetings = meetings.filter {
                $0.title.lowercased().contains(term) || $0.metadata.date.contains(term)
            }
        }
    }
    
    public func selectMeeting(_ meeting: MeetingSession) {
        self.selectedMeeting = meeting
        self.totalDuration = meeting.metadata.durationSeconds > 0 ? meeting.metadata.durationSeconds : meeting.duration
        self.currentTime = 0.0
        self.isPlaying = false
        
        // Initialiser AVPlayer si le média existe
        if let mediaURL = meeting.existingVideoURL ?? meeting.existingAudioURL {
            let p = AVPlayer(url: mediaURL)
            self.player = p
            
            // Observer la progression temporelle
            let interval = CMTime(seconds: 0.25, preferredTimescale: 600)
            self.timeObserver = p.addPeriodicTimeObserver(forInterval: interval, queue: .main) { [weak self] time in
                guard let self = self else { return }
                self.currentTime = time.seconds
                self.updateActiveSegment(at: time.seconds)
            }
        } else {
            self.player = nil
        }
    }
    
    public func play() {
        player?.play()
        isPlaying = true
    }
    
    public func pause() {
        player?.pause()
        isPlaying = false
    }
    
    public func stop() {
        player?.pause()
        player?.seek(to: .zero)
        currentTime = 0.0
        isPlaying = false
    }
    
    public func seek(to seconds: Double) {
        currentTime = seconds
        let target = CMTime(seconds: seconds, preferredTimescale: 600)
        player?.seek(to: target, toleranceBefore: .zero, toleranceAfter: .zero)
        updateActiveSegment(at: seconds)
    }
    
    private func updateActiveSegment(at seconds: Double) {
        guard let meeting = selectedMeeting else { return }
        activeSegment = meeting.transcriptSegments.first { seconds >= $0.start && seconds <= $0.end }
    }
    
    /// Relance la transcription Whisper sur la réunion courante
    public func retranscribeCurrentMeeting() async {
        guard let meeting = selectedMeeting, let audioURL = meeting.existingAudioURL else { return }
        isTranscribing = true
        let segments = await TranscriptionService.shared.transcribeAudioFile(audioURL: audioURL, language: "fr")
        meeting.transcriptSegments = segments
        await storage.saveTranscript(session: meeting)
        isTranscribing = false
    }
    
    /// Exporte la réunion sélectionnée en ZIP
    public func exportZip() async {
        guard let meeting = selectedMeeting else { return }
        
        #if os(macOS)
        let savePanel = NSSavePanel()
        savePanel.title = "Exporter la réunion en archive ZIP"
        savePanel.nameFieldStringValue = "\(meeting.title)_\(meeting.metadata.date).zip"
        savePanel.allowedContentTypes = [.zip]
        
        if savePanel.runModal() == .OK, let url = savePanel.url {
            let success = await storage.exportMeetingZip(session: meeting, destinationURL: url)
            exportMessage = success ? "✅ Archive exportée avec succès !" : "❌ Erreur lors de l'exportation."
        }
        #elseif os(iOS)
        let tempZip = FileManager.default.temporaryDirectory.appendingPathComponent("\(meeting.title).zip")
        let success = await storage.exportMeetingZip(session: meeting, destinationURL: tempZip)
        if success {
            exportMessage = "Archive prête pour partage : \(tempZip.lastPathComponent)"
        }
        #endif
    }
    
    /// Supprime définitivement la réunion
    public func deleteMeeting() async {
        guard let meeting = selectedMeeting else { return }
        let ok = await storage.deleteMeeting(session: meeting)
        if ok {
            await loadMeetings()
        }
    }
}
