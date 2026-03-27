using System.ComponentModel;
using System.Text.Json;
using HelpdeskAI.McpServer.Models;
using HelpdeskAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace HelpdeskAI.McpServer.Tools;

[McpServerToolType]
public static class SystemStatusTools
{
    private static string GetSeverityLabel(ServiceHealth h) => h switch
    {
        ServiceHealth.Outage => "OUTAGE",
        ServiceHealth.Degraded => "DEGRADED",
        ServiceHealth.Maintenance => "MAINTENANCE",
        _ => h.ToString().ToUpper(),
    };

    [McpServerTool(Name = "get_system_status")]
    [Description("""
        Checks the live health of IT services and infrastructure.
        Call this ONLY when the user explicitly asks about system status, service health, or outages.
        Returns operational status, incident IDs, and estimated resolution times.
        Optionally filter by service name or category.
        """)]
    public static string GetSystemStatus(
        SystemStatusService svc,
        [Description("Service name or keyword e.g. 'VPN', 'Teams', 'email'. Omit for all services.")] string? service = null,
        [Description("Category: Network | Email | Collaboration | DevTools | Business | Identity | Endpoint")] string? category = null,
        [Description("True = return only services with active incidents.")] bool incidentsOnly = false)
    {
        var list = svc.GetSystemStatus(service, category, incidentsOnly);
        if (list.Count == 0)
            return JsonSerializer.Serialize(new { message = service is not null ? $"No service found matching '{service}'." : "No services match the filters." });

        var items = list.Select(s => new
        {
            service    = s.Name,
            status     = s.Health.ToString().ToLowerInvariant(),
            incidentId = s.IncidentId,
            message    = s.StatusMessage,
            workaround = s.Workaround,
            eta        = s.EstimatedResolve.HasValue ? s.EstimatedResolve.Value.ToString("t") + " UTC" : null,
        }).ToList();

        var activeIncidents = items.Where(i => i.status != "operational").ToList();
        var result = new
        {
            checkedAt     = DateTimeOffset.UtcNow,
            totalServices = list.Count,
            activeCount   = activeIncidents.Count,
            services      = items,
        };

        if (activeIncidents.Count == 0)
            return JsonSerializer.Serialize(result);

        return JsonSerializer.Serialize(new
        {
            result.checkedAt,
            result.totalServices,
            result.activeCount,
            result.services,
            _renderAction = "show_incident_alert",
            _renderArgs   = new { incidents = JsonSerializer.Serialize(activeIncidents) },
        });
    }

    [McpServerTool(Name = "get_active_incidents")]
    [Description("""
        Returns all active IT incidents with full detail: impact description, affected teams,
        workarounds, and resolution ETAs.
        Call ONLY when the user explicitly asks about outages, incidents, or what is currently broken.
        """)]
    public static string GetActiveIncidents(SystemStatusService svc)
    {
        var incidents = svc.GetActiveIncidents();

        if (incidents.Count == 0)
            return JsonSerializer.Serialize(new { count = 0, message = "All IT systems operational." });

        var items = incidents.Select(s => new
        {
            service    = s.Name,
            severity   = GetSeverityLabel(s.Health).ToLowerInvariant(),
            incidentId = s.IncidentId,
            message    = s.StatusMessage,
            workaround = s.Workaround,
            eta        = s.EstimatedResolve.HasValue ? s.EstimatedResolve.Value.ToString("t") + " UTC" : null,
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            count         = incidents.Count,
            incidents     = items,
            _renderAction = "show_incident_alert",
            _renderArgs   = new { incidents = JsonSerializer.Serialize(items) },
        });
    }

    [McpServerTool(Name = "check_impact_for_team")]
    [Description("""
        Given a team or department name, returns all active incidents that affect them
        along with recommended workarounds. Use this to give a personalised incident
        summary  e.g. 'what issues are currently affecting the Engineering team?'
        or when a user reports widespread problems across their whole team.
        """)]
    public static string CheckImpactForTeam(
        SystemStatusService svc,
        [Description("Team or department name e.g. 'Engineering', 'Finance', 'Product', 'All'")] string team)
    {
        var affected = svc.GetImpactForTeam(team);

        if (affected.Count == 0)
            return JsonSerializer.Serialize(new
            {
                team,
                count = 0,
                message = $"No active incidents affecting the {team} team. All relevant systems are operational."
            });

        var items = affected.Select(s => new
        {
            service    = s.Name,
            severity   = GetSeverityLabel(s.Health).ToLowerInvariant(),
            incidentId = s.IncidentId,
            message    = s.StatusMessage,
            workaround = s.Workaround,
            eta        = s.EstimatedResolve.HasValue ? s.EstimatedResolve.Value.ToString("t") + " UTC" : null,
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            team,
            count         = items.Count,
            incidents     = items,
            _renderAction = "show_incident_alert",
            _renderArgs   = new { incidents = JsonSerializer.Serialize(items) },
        });
    }
}

