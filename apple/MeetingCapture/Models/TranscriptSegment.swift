import Foundation

/// Modèle miroir de TranscriptSegment C#
public struct TranscriptSegment: Identifiable, Codable, Equatable {
    public var id: UUID = UUID()
    public var start: Double
    public var end: Double
    public var speaker: String
    public var text: String
    
    enum CodingKeys: String, CodingKey {
        case start = "Start"
        case end = "End"
        case speaker = "Speaker"
        case text = "Text"
    }
    
    public init(start: Double, end: Double, speaker: String, text: String) {
        self.start = start
        self.end = end
        self.speaker = speaker
        self.text = text
    }
}
