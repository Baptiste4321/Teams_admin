import Foundation
import AVFoundation
#if os(macOS)
import ScreenCaptureKit
#elseif os(iOS)
import ReplayKit
#endif

/// Moteur universel de capture d'écran et audio isolé pour macOS (ScreenCaptureKit) et iOS (ReplayKit)
public class ScreenCaptureService: NSObject, ObservableObject {
    public static let shared = ScreenCaptureService()
    
    @Published public var isCapturing: Bool = false
    @Published public var availableWindows: [WindowInfo] = []
    @Published public var currentFps: Double = 30.0
    
    private var assetWriter: AVAssetWriter?
    private var videoInput: AVAssetWriterInput?
    private var audioInput: AVAssetWriterInput?
    private var sessionStartTime: CMTime?
    
    #if os(macOS)
    private var stream: SCStream?
    #endif
    
    public override init() {
        super.init()
    }
    
    // MARK: - Détection des fenêtres (macOS)
    public func refreshWindows() async {
        #if os(macOS)
        guard #available(macOS 12.3, *) else { return }
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            DispatchQueue.main.async {
                self.availableWindows = content.windows
                    .filter { $0.frame.width > 200 && $0.frame.height > 200 && $0.title?.isEmpty == false }
                    .map { scWin in
                        WindowInfo(
                            windowId: scWin.windowID,
                            title: scWin.title ?? "Sans titre",
                            processName: scWin.owningApplication?.applicationName ?? "Application",
                            bundleIdentifier: scWin.owningApplication?.bundleIdentifier ?? "",
                            frame: scWin.frame
                        )
                    }
            }
        } catch {
            print("[ScreenCaptureService] Erreur énumération fenêtres : \(error)")
        }
        #endif
    }
    
    // MARK: - Démarrage de la capture
    public func startCapture(targetWindow: WindowInfo?, outputVideoURL: URL, fps: Int = 30) async throws {
        let fm = FileManager.default
        if fm.fileExists(atPath: outputVideoURL.path) {
            try? fm.removeItem(at: outputVideoURL)
        }
        
        let writer = try AVAssetWriter(outputURL: outputVideoURL, fileType: .mp4)
        
        let videoSettings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: 1920,
            AVVideoHeightKey: 1080
        ]
        let vInput = AVAssetWriterInput(mediaType: .video, outputSettings: videoSettings)
        vInput.expectsMediaDataInRealTime = true
        writer.add(vInput)
        
        let audioSettings: [String: Any] = [
            AVFormatIDKey: kAudioFormatMPEG4AAC,
            AVNumberOfChannelsKey: 2,
            AVSampleRateKey: 48000,
            AVEncoderBitRateKey: 128000
        ]
        let aInput = AVAssetWriterInput(mediaType: .audio, outputSettings: audioSettings)
        aInput.expectsMediaDataInRealTime = true
        writer.add(aInput)
        
        writer.startWriting()
        self.assetWriter = writer
        self.videoInput = vInput
        self.audioInput = aInput
        self.sessionStartTime = nil
        
        #if os(macOS)
        if #available(macOS 12.3, *) {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            let filter: SCContentFilter
            
            if let target = targetWindow, let scWin = content.windows.first(where: { $0.windowID == target.windowId }) {
                // Capture isolée de la fenêtre ciblée (Teams)
                filter = SCContentFilter(desktopIndependentWindow: scWin)
            } else if let display = content.displays.first {
                filter = SCContentFilter(display: display, excludingWindows: [])
            } else {
                throw NSError(domain: "ScreenCaptureService", code: -1, userInfo: [NSLocalizedDescriptionKey: "Aucun affichage disponible"])
            }
            
            let config = SCStreamConfiguration()
            config.width = 1920
            config.height = 1080
            config.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(fps))
            config.queueDepth = 5
            config.showsCursor = true
            // Capture audio isolée de l'application Teams (Process loopback natif macOS !)
            config.capturesAudio = true
            config.excludesCurrentProcessAudio = true
            
            let scStream = SCStream(filter: filter, configuration: config, delegate: nil)
            try scStream.addStreamOutput(self, type: .screen, sampleHandlerQueue: DispatchQueue(label: "com.meetingcapture.video"))
            try scStream.addStreamOutput(self, type: .audio, sampleHandlerQueue: DispatchQueue(label: "com.meetingcapture.audio"))
            
            try await scStream.startCapture()
            self.stream = scStream
        }
        #elseif os(iOS)
        // ReplayKit pour iOS
        let recorder = RPScreenRecorder.shared()
        recorder.isMicrophoneEnabled = true
        try await recorder.startCapture { sampleBuffer, sampleType, error in
            guard error == nil else { return }
            self.handleSampleBuffer(sampleBuffer, ofType: sampleType)
        }
        #endif
        
        DispatchQueue.main.async {
            self.isCapturing = true
            self.currentFps = Double(fps)
        }
    }
    
    // MARK: - Arrêt de la capture
    public func stopCapture() async {
        #if os(macOS)
        if #available(macOS 12.3, *), let scStream = stream {
            try? await scStream.stopCapture()
            self.stream = nil
        }
        #elseif os(iOS)
        let recorder = RPScreenRecorder.shared()
        await withCheckedContinuation { continuation in
            recorder.stopCapture { _ in continuation.resume() }
        }
        #endif
        
        self.videoInput?.markAsFinished()
        self.audioInput?.markAsFinished()
        
        await withCheckedContinuation { continuation in
            self.assetWriter?.finishWriting {
                continuation.resume()
            }
        }
        
        DispatchQueue.main.async {
            self.isCapturing = false
        }
    }
}

// MARK: - Traitement des buffers ScreenCaptureKit (macOS)
#if os(macOS)
@available(macOS 12.3, *)
extension ScreenCaptureService: SCStreamOutput {
    public func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard isCapturing, let writer = assetWriter, writer.status == .writing else { return }
        
        if sessionStartTime == nil {
            let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
            writer.startSession(atSourceTime: pts)
            sessionStartTime = pts
        }
        
        switch type {
        case .screen:
            if videoInput?.isReadyForMoreMediaData == true {
                videoInput?.append(sampleBuffer)
            }
        case .audio:
            if audioInput?.isReadyForMoreMediaData == true {
                audioInput?.append(sampleBuffer)
            }
        @unknown default:
            break
        }
    }
}
#endif

#if os(iOS)
extension ScreenCaptureService {
    func handleSampleBuffer(_ sampleBuffer: CMSampleBuffer, ofType type: RPSampleBufferType) {
        guard isCapturing, let writer = assetWriter, writer.status == .writing else { return }
        
        if sessionStartTime == nil {
            let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
            writer.startSession(atSourceTime: pts)
            sessionStartTime = pts
        }
        
        switch type {
        case .video:
            if videoInput?.isReadyForMoreMediaData == true {
                videoInput?.append(sampleBuffer)
            }
        case .audioApp, .audioMic:
            if audioInput?.isReadyForMoreMediaData == true {
                audioInput?.append(sampleBuffer)
            }
        @unknown default:
            break
        }
    }
}
#endif
