using System.ComponentModel;
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
    public static string CreateTicket(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Short title (max 80 chars)")] string title,
        [Description("Full description including error messages and steps already tried")] string description,
        [Description("Low | Medium | High | Critical")] string priority,
        [Description("Hardware | Software | Network | Access | Email | VPN | Other")] string category,
        [Description("User's email address")] string requestedBy)
    {
        try
        {
            if (!Enum.TryParse<TicketPriority>(priority, true, out var p)) p = TicketPriority.Medium;
            if (!Enum.TryParse<TicketCategory>(category, true, out var c)) c = TicketCategory.Other;
            var t = svc.CreateTicket(title, description, p, c, requestedBy);
            return $"Ticket created: {t.Id} | {t.Priority} | {t.Category} | {t.Title}";
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "create_ticket failed for requestedBy={RequestedBy}", requestedBy);
            return $"Failed to create ticket: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_ticket")]
    [Description("Returns full details and all comments for a ticket by ID (e.g. INC-1001).")]
    public static string GetTicket(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID e.g. INC-1001")] string ticketId)
    {
        try
        {
            var t = svc.GetById(ticketId);
            if (t is null) return $"Not found: {ticketId}";
            string comments;
            lock (t)
            {
                comments = t.Comments.Count == 0
                    ? "  (none)"
                    : string.Join("\n", t.Comments.Select(c => $"  [{c.PostedAt:g}] {c.Author}: {c.Message}"));
            }
            return $"""
            {t.Id} - {t.Title}
            Status: {t.Status} | Priority: {t.Priority} | Category: {t.Category}
            Requested by: {t.RequestedBy} | Assigned: {t.AssignedTo ?? "Unassigned"}
            Created: {t.CreatedAt:R} | Updated: {t.UpdatedAt:R}
            Resolution: {t.Resolution ?? "N/A"}
            Description: {t.Description}
            Comments:
            {comments}
            """;
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "get_ticket failed for ticketId={TicketId}", ticketId);
            return $"Failed to retrieve ticket {ticketId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "search_tickets")]
    [Description("Searches tickets by email, status, or category. Returns up to 15 results.")]
    public static string SearchTickets(
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
            var results = svc.Search(requestedBy, s, c);
            return results.Count == 0
                ? "No tickets found."
                : $"Found {results.Count}:\n" + string.Join("\n", results.Select(t =>
                    $"  {t.Id} | {t.Status,-12} | {t.Priority,-8} | {t.Title}"));
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "search_tickets failed");
            return $"Failed to search tickets: {ex.Message}";
        }
    }

    [McpServerTool(Name = "update_ticket_status")]
    [Description("Updates ticket status. Use Resolved with a resolution note when the issue is fixed.")]
    public static string UpdateTicketStatus(
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
            var t = svc.UpdateStatus(ticketId, status, resolution);
            return t is null
                ? $"Not found: {ticketId}"
                : $"{t.Id} updated  {t.Status}. Resolution: {t.Resolution ?? "N/A"}";
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TicketTools").LogError(ex, "update_ticket_status failed for ticketId={TicketId}", ticketId);
            return $"Failed to update ticket {ticketId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "add_ticket_comment")]
    [Description("Adds a comment to a ticket. Set isInternal=true for IT-only notes.")]
    public static string AddTicketComment(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID")] string ticketId,
        [Description("Author name or email")] string author,
        [Description("Comment text")] string message,
        [Description("True = internal IT note, false = user-visible")] bool isInternal = false)
    {
        try
        {
            var t = svc.AddComment(ticketId, author, message, isInternal);
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
    public static string AssignTicket(
        TicketService svc,
        ILoggerFactory loggerFactory,
        [Description("Ticket ID")] string ticketId,
        [Description("Email or name of the agent or team to assign to, e.g. helpdesk-tier2@contoso.com")] string assignTo)
    {
        try
        {
            var t = svc.AssignTicket(ticketId, assignTo);
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
