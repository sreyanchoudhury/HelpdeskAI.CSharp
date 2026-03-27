using System.Text.Json.Serialization;

namespace HelpdeskAI.McpServer.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketStatus { Open, InProgress, PendingUser, Resolved, Closed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketPriority { Low, Medium, High, Critical }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketCategory { Hardware, Software, Network, Access, Email, VPN, Other }

public sealed class Ticket
{
    public string Id { get; init; } = string.Empty;
    /// <summary>Monotonically-increasing sequence number used to generate INC-NNNN IDs.</summary>
    public int Seq { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketCategory Category { get; set; } = TicketCategory.Other;
    public string RequestedBy { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }
    public string? UserSentiment { get; set; }
    public string? EscalationReason { get; set; }
    public string? ImpactScope { get; set; }
    public List<string> RelatedIncidentIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TicketComment> Comments { get; set; } = [];
}

public sealed class TicketComment
{
    public string Author { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
    public DateTimeOffset PostedAt { get; init; } = DateTimeOffset.UtcNow;
}
