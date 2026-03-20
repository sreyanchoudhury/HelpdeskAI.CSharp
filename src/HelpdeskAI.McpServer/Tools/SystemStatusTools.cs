using System.ComponentModel;
using System.Text;
using System.Text.Json;
using HelpdeskAI.McpServer.Models;
using ModelContextProtocol.Server;

namespace HelpdeskAI.McpServer.Tools;

[McpServerToolType]
public static class SystemStatusTools
{
    private const int SeparatorWidth = 52;

    private static readonly List<ServiceStatus> Services = BuildSeed();

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
        [Description("Service name or keyword e.g. 'VPN', 'Teams', 'email'. Omit for all services.")] string? service = null,
        [Description("Category: Network | Email | Collaboration | DevTools | Business | Identity | Endpoint")] string? category = null,
        [Description("True = return only services with active incidents.")] bool incidentsOnly = false)
    {
        var results = Services.AsEnumerable();

        if (incidentsOnly)
            results = results.Where(s => s.Health != ServiceHealth.Operational);
        else if (service is { Length: > 0 })
            results = results.Where(s => s.Name.Contains(service, StringComparison.OrdinalIgnoreCase));
        else if (category is { Length: > 0 })
            results = results.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var list = results.OrderBy(s => s.Health).ToList();
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
    public static string GetActiveIncidents()
    {
        var incidents = Services
            .Where(s => s.Health != ServiceHealth.Operational)
            .OrderBy(s => s.Health)
            .ToList();

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
        [Description("Team or department name e.g. 'Engineering', 'Finance', 'Product', 'All'")] string team)
    {
        var matchAll = team.Equals("All", StringComparison.OrdinalIgnoreCase);

        var affected = Services
            .Where(s => s.Health != ServiceHealth.Operational)
            .Where(s => matchAll ||
                        s.AffectedTeams.Length == 0 ||   // incidents with no specific scope affect everyone
                        s.AffectedTeams.Any(t => t.Contains(team, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Health)
            .ToList();

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

    private static List<ServiceStatus> BuildSeed()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new() { Name = "Microsoft Exchange Online", Category = "Email",
                    Health = ServiceHealth.Operational },

            new() { Name = "Microsoft Teams",           Category = "Collaboration",
                    Health = ServiceHealth.Degraded,
                    StatusMessage = "Intermittent call drops in APAC region.",
                    IncidentId = "INC-9041",
                    IncidentStarted = now.AddHours(-2), EstimatedResolve = now.AddHours(1),
                    AffectedTeams = ["Engineering", "Product", "Sales"],
                    Workaround = "Use Teams web app at teams.microsoft.com or dial in via phone audio." },

            new() { Name = "SharePoint Online",         Category = "Collaboration",
                    Health = ServiceHealth.Operational },

            new() { Name = "Microsoft Entra ID",        Category = "Identity",
                    Health = ServiceHealth.Operational },

            new() { Name = "VPN Gateway (Kolkata)",     Category = "Network",
                    Health = ServiceHealth.Outage,
                    StatusMessage = "Primary gateway offline. Failover in progress.",
                    IncidentId = "INC-9055",
                    IncidentStarted = now.AddMinutes(-45), EstimatedResolve = now.AddHours(2),
                    AffectedTeams = ["Engineering", "Finance", "HR"],
                    Workaround = "Connect to vpn2.contoso.com (secondary gateway) instead." },

            new() { Name = "VPN Gateway (Global)",      Category = "Network",
                    Health = ServiceHealth.Operational },

            new() { Name = "GitHub Enterprise",         Category = "DevTools",
                    Health = ServiceHealth.Operational },

            new() { Name = "Azure DevOps",              Category = "DevTools",
                    Health = ServiceHealth.Maintenance,
                    StatusMessage = "Scheduled maintenance  pipeline agent pool upgrades.",
                    IncidentStarted = now.AddMinutes(-20), EstimatedResolve = now.AddHours(3),
                    AffectedTeams = ["Engineering"],
                    Workaround = "Queue builds manually after maintenance window. Read-only access still available." },

            new() { Name = "Corporate Wi-Fi (Kolkata)", Category = "Network",
                    Health = ServiceHealth.Operational },

            new() { Name = "Intune / MDM",              Category = "Endpoint",
                    Health = ServiceHealth.Operational },

            new() { Name = "SAP ERP",                   Category = "Business",
                    Health = ServiceHealth.Degraded,
                    StatusMessage = "Slow response in Finance module. DB team investigating.",
                    IncidentId = "INC-9048",
                    IncidentStarted = now.AddHours(-1), EstimatedResolve = now.AddHours(4),
                    AffectedTeams = ["Finance", "Operations"],
                    Workaround = "Use SAP GUI offline mode for read-only reporting. Avoid posting transactions until resolved." },

            new() { Name = "Salesforce CRM",            Category = "Business",
                    Health = ServiceHealth.Operational },

            new() { Name = "ServiceNow",                Category = "ITSM",
                    Health = ServiceHealth.Operational },
        ];
    }
}

