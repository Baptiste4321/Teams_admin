import Foundation
import Speech

/// Service de transcription audio pour Apple (Whisper Python + Apple Speech Framework)
public class TranscriptionService: ObservableObject {
    public static let shared = TranscriptionService()
    
    @Published public var isTranscribing: Bool = false
    
    public init() {}
    
    /// Transcrit un fichier audio via Whisper Python local (si disponible) ou Apple Speech Framework
    public func transcribeAudioFile(audioURL: URL, language: String = "fr") async -> [TranscriptSegment] {
        await MainActor.run { isTranscribing = true }
        defer {
            Task { @MainActor in isTranscribing = false }
        }
        
        #if os(macOS)
        // 1. Tenter l'exécution du script Whisper Python si Python est installé sur le Mac
        if let whisperSegments = await runPythonWhisperScript(audioURL: audioURL, language: language), !whisperSegments.isEmpty {
            return whisperSegments
        }
        #endif
        
        // 2. Fallback natif Apple Speech Framework (SFSpeechRecognizer)
        return await transcribeViaAppleSpeech(audioURL: audioURL, locale: Locale(identifier: language == "fr" ? "fr-FR" : "en-US"))
    }
    
    #if os(macOS)
    private func runPythonWhisperScript(audioURL: URL, language: String) async -> [TranscriptSegment]? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        
        // Recherche du script scripts/transcribe_wav.py
        let possibleScriptPaths = [
            Bundle.main.resourceURL?.appendingPathComponent("scripts/transcribe_wav.py").path,
            Bundle.main.bundleURL.appendingPathComponent("scripts/transcribe_wav.py").path,
            "scripts/transcribe_wav.py"
        ].compactMap { $0 }
        
        guard let scriptPath = possibleScriptPaths.first(where: { FileManager.default.fileExists(atPath: $0) }) else {
            return nil
        }
        
        process.arguments = ["python3", scriptPath, audioURL.path, "--language", language]
        
        let pipe = Pipe()
        process.standardOutput = pipe
        
        do {
            try process.run()
            process.waitUntilExit()
            
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            let segments = try JSONDecoder().decode([TranscriptSegment].self, from: data)
            return segments
        } catch {
            print("[TranscriptionService] Python Whisper non exécuté : \(error)")
            return nil
        }
    }
    #endif
    
    private func transcribeViaAppleSpeech(audioURL: URL, locale: Locale) async -> [TranscriptSegment] {
        guard let recognizer = SFSpeechRecognizer(locale: locale), recognizer.isAvailable else {
            return []
        }
        
        let request = SFSpeechURLRecognitionRequest(url: audioURL)
        request.shouldReportPartialResults = false
        
        return await withCheckedContinuation { continuation in
            recognizer.recognitionTask(with: request) { result, error in
                guard let result = result, result.isFinal else {
                    if error != nil {
                        continuation.resume(returning: [])
                    }
                    return
                }
                
                var segments: [TranscriptSegment] = []
                for (index, segment) in result.bestTranscription.segments.enumerated() {
                    let seg = TranscriptSegment(
                        start: segment.timestamp,
                        end: segment.timestamp + segment.duration,
                        speaker: "Intervenant \(index % 2 + 1)",
                        text: segment.substring
                    )
                    segments.append(seg)
                }
                continuation.resume(returning: segments)
            }
        }
    }
}
