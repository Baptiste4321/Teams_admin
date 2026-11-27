using System.Text.Json;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Chat;

public class ChatCaptureService : IChatCaptureService
{
    private readonly List<ChatMessage> _messages = new();
    private readonly object _lock = new();
    private CaptureStatus _status = CaptureStatus.Idle;

    public CaptureStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, _status);
            }
        }
    }

    public int MessagesCount
    {
        get
        {
            lock (_lock) return _messages.Count;
        }
    }

    public string SourceType { get; private set; } = "Teams Realtime / Import Hook";

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<CaptureStatus>? StatusChanged;

    public Task<bool> StartAsync(ChatCaptureOptions options, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _messages.Clear();
            SourceType = options.SourceType;
            Status = CaptureStatus.Capturing;
        }
        return Task.FromResult(true);
    }

    public void AddMessage(ChatMessage message)
    {
        lock (_lock)
        {
            _messages.Add(message);
        }
        MessageReceived?.Invoke(this, message);
    }

    public async Task ImportChatMessagesAsync(string filePathOrContent)
    {
        string content;
        if (File.Exists(filePathOrContent))
        {
            content = await File.ReadAllTextAsync(filePathOrContent);
        }
        else
        {
            content = filePathOrContent;
        }

        try
        {
            var imported = JsonSerializer.Deserialize<List<ChatMessage>>(content);
            if (imported != null)
            {
                foreach (var msg in imported)
                {
                    AddMessage(msg);
                }
            }
        }
        catch
        {
            // Parse plain text lines
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var msg = new ChatMessage
                {
                    Author = "Participant",
                    Text = line,
                    TimestampUtc = DateTime.UtcNow,
                    MessageType = "standard"
                };
                AddMessage(msg);
            }
        }
    }

    public IReadOnlyList<ChatMessage> GetMessages()
    {
        lock (_lock)
        {
            return _messages.ToList();
        }
    }

    public Task StopAsync()
    {
        lock (_lock)
        {
            Status = CaptureStatus.Stopped;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
