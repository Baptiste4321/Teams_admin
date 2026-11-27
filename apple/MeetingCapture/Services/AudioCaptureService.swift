import Foundation
import AVFoundation

/// Service de capture audio et monitoring des VU-mètres (SYS & MIC)
public class AudioCaptureService: ObservableObject {
    public static let shared = AudioCaptureService()
    
    @Published public var systemAudioLevel: Float = 0.0
    @Published public var micAudioLevel: Float = 0.0
    @Published public var isMicEnabled: Bool = false
    
    private var audioEngine: AVAudioEngine?
    
    public init() {}
    
    /// Démarre le moniteur VU-mètre en direct
    public func startMonitoring() {
        let engine = AVAudioEngine()
        let inputNode = engine.inputNode
        let format = inputNode.outputFormat(forBus: 0)
        
        inputNode.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
            guard let self = self else { return }
            let level = self.calculateRMS(buffer: buffer)
            DispatchQueue.main.async {
                self.micAudioLevel = min(1.0, level * 5.0)
                // Simulation légère du flux loopback en cours
                if ScreenCaptureService.shared.isCapturing {
                    self.systemAudioLevel = min(1.0, max(0.05, Float.random(in: 0.1...0.6)))
                } else {
                    self.systemAudioLevel = 0.0
                }
            }
        }
        
        try? engine.start()
        self.audioEngine = engine
    }
    
    public func stopMonitoring() {
        audioEngine?.stop()
        audioEngine?.inputNode.removeTap(onBus: 0)
        audioEngine = nil
        systemAudioLevel = 0.0
        micAudioLevel = 0.0
    }
    
    private func calculateRMS(buffer: AVAudioPCMBuffer) -> Float {
        guard let channelData = buffer.floatChannelData?[0] else { return 0 }
        let frameLength = UInt(buffer.frameLength)
        var sum: Float = 0.0
        for i in 0..<Int(frameLength) {
            sum += channelData[i] * channelData[i]
        }
        return sqrt(sum / Float(frameLength))
    }
}
