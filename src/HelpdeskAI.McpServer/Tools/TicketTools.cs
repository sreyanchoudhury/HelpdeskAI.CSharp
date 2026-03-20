using System.ComponentModel;
using System.Text.Json;
using HelpdeskAI.McpServer.Models;
using HelpdeskAI.McpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace HelpdeskAI.McpServer.Tools;

[McpServerToolType]
public static class TicketTools
{
    [McpServerTool(Name = "create_ticket")]
    [Description("Creates a new IT support ticket. Call this when the user reports a new issue that needs tracking.")]
    public static async Task<string> CreateTicket(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Short title (max 80 chars)")] string title,
        [Description("Full description including error messages and steps already tried")] string description,
        [Description("Hardware | Software | Network | Access | Email | VPN | Other")] string category,
        [Description("User's email address")] string requestedBy,
        [Description("Low | Medium | High | Critical — defaults to Medium if not specified")] string priority = "Medium")
    {
        try
        {
            if (!Enum.TryParse<TicketPriority>(priority, true, out var p)) p = TicketPriority.Medium;
            if (!Enum.TryParse<TicketCategory>(category, true, out var c)) c = TicketCategory.Other;
            var t = await svc.CreateTicketAsync(title, description, p, c, requestedBy);
            var pri = t.Priority.ToString().ToLowerInvariant();
            var cat = t.Category.ToString().ToLowerInvariant();
            return JsonSerializer.Serialize(new
            {
                id          = t.Id,
                title       = t.Title,
                status      = t.Status.ToString().ToLowerInvariant(),
                priority    = pri,
                category    = cat,
                description = t.Description,
                requestedBy = t.RequestedBy,
                assignedTo  = t.AssignedTo,
                createdAt   = t.CreatedAt,
                _renderAction = "show_ticket_created",
                _renderArgs   = new { id = t.Id, title = t.Title, description = t.Description, priority = pri, category = cat },
            });
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "create_ticket failed for requestedBy={RequestedBy}", requestedBy);
            return $"Failed to create ticket: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_ticket")]
    [Description("Returns full details and all comments for a ticket by ID (e.g. INC-1001). Returns JSON.")]
    public static async Task<string> GetTicket(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID e.g. INC-1001")] string ticketId)
    {
        try
        {
            var t = await svc.GetByIdAsync(ticketId);
            if (t is null) return $"{{\"error\":\"Not found: {ticketId}\"}}";
            var comments = t.Comments
                .Select(c => (object)new { postedAt = c.PostedAt, author = c.Author, message = c.Message, isInternal = c.IsInternal })
                .ToList();
            var pri = t.Priority.ToString().ToLowerInvariant();
            var cat = t.Category.ToString().ToLowerInvariant();
            var sta = t.Status.ToString().ToLowerInvariant();
            return JsonSerializer.Serialize(new
            {
                id          = t.Id,
                title       = t.Title,
                status      = sta,
                priority    = pri,
                category    = cat,
                description = t.Description,
                requestedBy = t.RequestedBy,
                assignedTo  = t.AssignedTo,
                createdAt   = t.CreatedAt,
                updatedAt   = t.UpdatedAt,
                resolution  = t.Resolution,
                comments,
                _renderAction = "show_ticket_details",
                _renderArgs   = new { id = t.Id, title = t.Title, description = t.Description, priority = pri, category = cat, status = sta, assignedTo = t.AssignedTo, createdAt = t.CreatedAt.ToString("o") },
            });
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "get_ticket failed for ticketId={TicketId}", ticketId);
            return $"{{\"error\":\"Failed to retrieve ticket {ticketId}: {ex.Message}\"}}";
        }
    }

    [McpServerTool(Name = "search_tickets")]
    [Description("Searches tickets by email, status, or category. Returns up to 15 results as JSON.")]
    public static async Task<string> SearchTickets(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Filter by submitter email (optional)")] string? requestedBy = null,
        [Description("Open | InProgress | PendingUser | Resolved | Closed (optional)")] string? status = null,
        [Description("Hardware | Software | Network | Access | Email | VPN | Other (optional)")] string? category = null)
    {
        try
        {
            TicketStatus? s = status is { Length: > 0 } && Enum.TryParse<TicketStatus>(status, true, out var sv) ? sv : null;
            TicketCategory? c = category is { Length: > 0 } && Enum.TryParse<TicketCategory>(category, true, out var cv) ? cv : null;
            var results = await svc.SearchAsync(requestedBy, s, c);
            var ticketList = results.Select(t => new
            {
                id       = t.Id,
                title    = t.Title,
                status   = t.Status.ToString().ToLowerInvariant(),
                priority = t.Priority.ToString().ToLowerInvariant(),
                category = t.Category.ToString().ToLowerInvariant(),
            }).ToList();
            return JsonSerializer.Serialize(new
            {
                count   = results.Count,
                tickets = ticketList,
                _renderAction = "show_my_tickets",
                _renderArgs   = new { tickets = ticketList },
            });
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "search_tickets failed");
            return $"{{\"error\":\"Failed to search tickets: {ex.Message}\"}}";
        }
    }

    [McpServerTool(Name = "update_ticket_status")]
    [Description("Updates ticket status. Use Resolved with a resolution note when the issue is fixed.")]
    public static async Task<string> UpdateTicketStatus(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID")] string ticketId,
        [Description("Open | InProgress | PendingUser | Resolved | Closed")] string newStatus,
        [Description("Resolution notes - required when setting Resolved or Closed")] string? resolution = null)
    {
        try
        {
            if (!Enum.TryParse<TicketStatus>(newStatus, true, out var status))
                return $"Invalid status '{newStatus}'. Valid: Open, InProgress, PendingUser, Resolved, Closed";
            var t = await svc.UpdateStatusAsync(ticketId, status, resolution);
            return t is null
                ? $"Not found: {ticketId}"
                : $"{t.Id} updated → {t.Status}. Resolution: {t.Resolution ?? "N/A"}";
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "update_ticket_status failed for ticketId={TicketId}", ticketId);
            return $"Failed to update ticket {ticketId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "add_ticket_comment")]
    [Description("Adds a comment to a ticket. Set isInternal=true for IT-only notes.")]
    public static async Task<string> AddTicketComment(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID")] string ticketId,
        [Description("Author name or email")] string author,
        [Description("Comment text")] string message,
        [Description("True = internal IT note, false = user-visible")] bool isInternal = false)
    {
        try
        {
            var t = await svc.AddCommentAsync(ticketId, author, message, isInternal);
            return t is null ? $"Not found: {ticketId}" : $"Comment added to {ticketId}. Total comments: {t.Comments.Count}";
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "add_ticket_comment failed for ticketId={TicketId}", ticketId);
            return $"Failed to add comment to ticket {ticketId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "assign_ticket")]
    [Description("Assigns a ticket to a support agent or team. Use when escalating or routing to a specific person or queue.")]
    public static async Task<string> AssignTicket(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID")] string ticketId,
        [Description("Email or name of the agent or team to assign to, e.g. helpdesk-tier2@contoso.com")] string assignTo)
    {
        try
        {
            var t = await svc.AssignTicketAsync(ticketId, assignTo);
            return t is null
                ? $"Not found: {ticketId}"
                : $"{t.Id} assigned to {t.AssignedTo}. Status: {t.Status}";
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "assign_ticket failed for ticketId={TicketId}", ticketId);
            return $"Failed to assign ticket {ticketId}: {ex.Message}";
        }
    }
}
