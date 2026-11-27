import Foundation

/// Modèle d'élément d'action
public struct ActionItem: Identifiable, Codable, Equatable {
    public var id: UUID = UUID()
    public var task: String
    public var assignee: String
    public var priority: String
    
    enum CodingKeys: String, CodingKey {
        case task = "Task"
        case assignee = "Assignee"
        case priority = "Priority"
    }
}

/// Analyse IA de la réunion
public struct AiAnalysisResult: Codable, Equatable {
    public var executiveSummary: String?
    public var keyDecisions: [String] = []
    public var actionItems: [ActionItem] = []
    
    enum CodingKeys: String, CodingKey {
        case executiveSummary = "ExecutiveSummary"
        case keyDecisions = "KeyDecisions"
        case actionItems = "ActionItems"
    }
}

/// Métadonnées complètes de la réunion (miroir meeting.json)
public struct MeetingMetadata: Codable, Equatable {
    public var id: String
    public var name: String
    public var date: String
    public var startTimeUtc: Date
    public var endTimeUtc: Date?
    public var durationSeconds: Double = 0.0
    public var softwareVersion: String = "1.0.0"
    public var resolution: String = "1920x1080"
    public var fps: Double = 30.0
    public var videoCodec: String = "H.264"
    public var audioCodec: String = "AAC"
    public var platform: String = "Apple"
    public var capturedSources: [String] = ["Video", "AudioLoopback"]
    public var statuses: [String: String] = [:]
    
    enum CodingKeys: String, CodingKey {
        case id = "Id"
        case name = "Name"
        case date = "Date"
        case startTimeUtc = "StartTimeUtc"
        case endTimeUtc = "EndTimeUtc"
        case durationSeconds = "DurationSeconds"
        case softwareVersion = "SoftwareVersion"
        case resolution = "Resolution"
        case fps = "Fps"
        case videoCodec = "VideoCodec"
        case audioCodec = "AudioCodec"
        case platform = "Platform"
        case capturedSources = "CapturedSources"
        case statuses = "Statuses"
    }
}
