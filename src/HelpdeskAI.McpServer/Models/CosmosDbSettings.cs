namespace HelpdeskAI.McpServer.Models;

public sealed class CosmosDbSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string PrimaryKey { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "helpdeskdb";
    public string ContainerName { get; set; } = "tickets";
}
