import Foundation

/// Modèle miroir de ChatMessage C#
public struct ChatMessage: Identifiable, Codable, Equatable {
    public var id: UUID = UUID()
    public var timestampUtc: Date
    public var author: String
    public var text: String
    
    enum CodingKeys: String, CodingKey {
        case timestampUtc = "TimestampUtc"
        case author = "Author"
        case text = "Text"
    }
    
    public init(timestampUtc: Date = Date(), author: String, text: String) {
        self.timestampUtc = timestampUtc
        self.author = author
        self.text = text
    }
}
