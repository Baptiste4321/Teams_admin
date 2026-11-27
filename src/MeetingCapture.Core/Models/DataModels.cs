namespace MeetingCapture.Core.Models;

public class TranscriptSegment
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Speaker { get; set; } = "Intervenant";
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public string Language { get; set; } = "fr";
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public double RelativeTimeSeconds { get; set; }
    public string? ReplyToId { get; set; }
    public List<string> Links { get; set; } = new();
    public List<string> Attachments { get; set; } = new();
    public string MessageType { get; set; } = "standard"; // standard, announcement, code, file
}

public class ActionItem
{
    public string Task { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string Priority { get; set; } = "Moyenne"; // Haute, Moyenne, Basse
}

public class AiAnalysisResult
{
    public string ExecutiveSummary { get; set; } = string.Empty;
    public List<string> KeyDecisions { get; set; } = new();
    public List<ActionItem> ActionItems { get; set; } = new();
    public List<string> TopicsDiscussed { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SyncEvent
{
    public DateTime UtcTime { get; set; }
    public DateTime LocalTime { get; set; }
    public double RelativeSeconds { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Details { get; set; }
}
