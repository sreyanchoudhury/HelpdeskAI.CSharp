using HelpdeskAI.McpServer.Models;

namespace HelpdeskAI.McpServer.Services;

public sealed class SystemStatusService
{
    private readonly List<ServiceStatus> _services = BuildSeed();

    public IReadOnlyList<ServiceStatus> GetSystemStatus(string? service = null, string? category = null, bool incidentsOnly = false)
    {
        var results = _services.AsEnumerable();

        if (incidentsOnly)
            results = results.Where(s => s.Health != ServiceHealth.Operational);
        else if (service is { Length: > 0 })
            results = results.Where(s => s.Name.Contains(service, StringComparison.OrdinalIgnoreCase));
        else if (category is { Length: > 0 })
            results = results.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        return results.OrderBy(s => s.Health).ToList();
    }

    public IReadOnlyList<ServiceStatus> GetActiveIncidents() =>
        _services
            .Where(s => s.Health != ServiceHealth.Operational)
            .OrderBy(s => s.Health)
            .ToList();

    public IReadOnlyList<ServiceStatus> GetImpactForTeam(string team)
    {
        var matchAll = team.Equals("All", StringComparison.OrdinalIgnoreCase);

        return _services
            .Where(s => s.Health != ServiceHealth.Operational)
            .Where(s => matchAll ||
                        s.AffectedTeams.Length == 0 ||
                        s.AffectedTeams.Any(t => t.Contains(team, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Health)
            .ToList();
    }

    private static List<ServiceStatus> BuildSeed()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new() { Name = "Microsoft Exchange Online", Category = "Email", Health = ServiceHealth.Operational },
            new() { Name = "Microsoft Teams", Category = "Collaboration", Health = ServiceHealth.Degraded, StatusMessage = "Intermittent call drops in APAC region.", IncidentId = "INC-9041", IncidentStarted = now.AddHours(-2), EstimatedResolve = now.AddHours(1), AffectedTeams = ["Engineering", "Product", "Sales"], Workaround = "Use Teams web app at teams.microsoft.com or dial in via phone audio." },
            new() { Name = "SharePoint Online", Category = "Collaboration", Health = ServiceHealth.Operational },
            new() { Name = "Microsoft Entra ID", Category = "Identity", Health = ServiceHealth.Operational },
            new() { Name = "VPN Gateway (Kolkata)", Category = "Network", Health = ServiceHealth.Outage, StatusMessage = "Primary gateway offline. Failover in progress.", IncidentId = "INC-9055", IncidentStarted = now.AddMinutes(-45), EstimatedResolve = now.AddHours(2), AffectedTeams = ["Engineering", "Finance", "HR"], Workaround = "Connect to vpn2.contoso.com (secondary gateway) instead." },
            new() { Name = "VPN Gateway (Global)", Category = "Network", Health = ServiceHealth.Operational },
            new() { Name = "GitHub Enterprise", Category = "DevTools", Health = ServiceHealth.Operational },
            new() { Name = "Azure DevOps", Category = "DevTools", Health = ServiceHealth.Maintenance, StatusMessage = "Scheduled maintenance pipeline agent pool upgrades.", IncidentStarted = now.AddMinutes(-20), EstimatedResolve = now.AddHours(3), AffectedTeams = ["Engineering"], Workaround = "Queue builds manually after maintenance window. Read-only access still available." },
            new() { Name = "Corporate Wi-Fi (Kolkata)", Category = "Network", Health = ServiceHealth.Operational },
            new() { Name = "Intune / MDM", Category = "Endpoint", Health = ServiceHealth.Operational },
            new() { Name = "SAP ERP", Category = "Business", Health = ServiceHealth.Degraded, StatusMessage = "Slow response in Finance module. DB team investigating.", IncidentId = "INC-9048", IncidentStarted = now.AddHours(-1), EstimatedResolve = now.AddHours(4), AffectedTeams = ["Finance", "Operations"], Workaround = "Use SAP GUI offline mode for read-only reporting. Avoid posting transactions until resolved." },
            new() { Name = "Salesforce CRM", Category = "Business", Health = ServiceHealth.Operational },
            new() { Name = "ServiceNow", Category = "ITSM", Health = ServiceHealth.Operational },
        ];
    }
}
