namespace HelpdeskAI.McpServer.Models;

public enum ServiceHealth { Operational, Degraded, Outage, Maintenance }

public sealed class ServiceStatus
{
	public string Name { get; init; } = string.Empty;
	public string Category { get; init; } = string.Empty;
	public ServiceHealth Health { get; init; } = ServiceHealth.Operational;
	public string? StatusMessage { get; init; }
	public string? IncidentId { get; init; }
	public DateTimeOffset? IncidentStarted { get; init; }
	public DateTimeOffset? EstimatedResolve { get; init; }
	public string[] AffectedTeams { get; init; } = [];
	public string? Workaround { get; init; }
}
