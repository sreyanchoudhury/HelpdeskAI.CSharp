using HelpdeskAI.McpServer.Models;
using Microsoft.Azure.Cosmos;

namespace HelpdeskAI.McpServer.Services;

// NOTE: McpServer is pinned to 1 replica (minReplicas=1 in apps.bicep).
// Interlocked.Increment on _nextId is therefore safe across all concurrent requests on this instance.
public sealed class TicketService
{
    private const int MaxSearchResults = 15;
    private const int InitialSequence  = 1000;

    private readonly Container              _container;
    private readonly ILogger<TicketService> _log;
    private          int                    _nextId;

    public TicketService(Container container, ILogger<TicketService> log)
    {
        _container = container;
        _log       = log;
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once at startup from Program.cs via IHostedService or app.Services.
    /// Restores the ID counter and conditionally seeds the container.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Count existing documents
        int count = await ScalarQueryAsync<int>("SELECT VALUE COUNT(1) FROM c", ct);

        if (count == 0)
        {
            _log.LogInformation("Cosmos container is empty — seeding tickets...");
            _nextId = InitialSequence;
            await SeedAsync(ct);
            _log.LogInformation("Seeded {Count} tickets", _nextId - InitialSequence);
            return;
        }

        // Restore ID counter from the highest seq value in the container
        int maxSeq = await ScalarQueryAsync<int>("SELECT VALUE MAX(c.seq) FROM c", ct);
        _nextId = maxSeq == 0 ? InitialSequence : maxSeq;
        _log.LogInformation("Cosmos: {Count} ticket(s) found. Resuming from INC-{Next}", count, _nextId + 1);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    public async Task<Ticket> CreateTicketAsync(
        string title, string description,
        TicketPriority priority, TicketCategory category,
        string requestedBy, CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _nextId);
        var id  = $"INC-{seq}";
        var ticket = new Ticket
        {
            Id          = id,
            Seq         = seq,
            Title       = title,
            Description = description,
            Priority    = priority,
            Category    = category,
            RequestedBy = requestedBy,
        };
        await _container.CreateItemAsync(ticket, new PartitionKey(id), cancellationToken: ct);
        return ticket;
    }

    public async Task<Ticket?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<Ticket>(id, new PartitionKey(id), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Ticket>> SearchAsync(
        string? requestedBy, TicketStatus? status, TicketCategory? category,
        CancellationToken ct = default)
    {
        // Build parameterised SQL dynamically — data always in parameters, never in SQL text
        var sql = "SELECT * FROM c WHERE 1=1"
            + (requestedBy is not null ? " AND CONTAINS(LOWER(c.requestedBy), LOWER(@requestedBy))" : "")
            + (status      is not null ? " AND c.status   = @status"   : "")
            + (category    is not null ? " AND c.category = @category" : "")
            + " ORDER BY c.createdAt DESC";

        var qd = new QueryDefinition(sql);
        if (requestedBy is not null) qd = qd.WithParameter("@requestedBy", requestedBy);
        if (status      is not null) qd = qd.WithParameter("@status",      status.ToString());
        if (category    is not null) qd = qd.WithParameter("@category",    category.ToString());

        var opts    = new QueryRequestOptions { MaxItemCount = MaxSearchResults };
        using var iter    = _container.GetItemQueryIterator<Ticket>(qd, requestOptions: opts);
        var results = new List<Ticket>(MaxSearchResults);
        while (iter.HasMoreResults && results.Count < MaxSearchResults)
        {
            var page = await iter.ReadNextAsync(ct);
            results.AddRange(page);
        }
        return results.Take(MaxSearchResults).ToList();
    }

    public async Task<Ticket?> UpdateStatusAsync(
        string id, TicketStatus status, string? resolution, CancellationToken ct = default)
    {
        var patches = new List<PatchOperation>
        {
            PatchOperation.Set("/status",    status.ToString()),
            PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow),
        };
        if (resolution is not null)
            patches.Add(PatchOperation.Set("/resolution", resolution));

        try
        {
            var r = await _container.PatchItemAsync<Ticket>(id, new PartitionKey(id), patches, cancellationToken: ct);
            return r.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Ticket?> AddCommentAsync(
        string id, string author, string message, bool isInternal, CancellationToken ct = default)
    {
        var comment = new TicketComment { Author = author, Message = message, IsInternal = isInternal };
        var patches = new PatchOperation[]
        {
            PatchOperation.Add("/comments/-", comment),
            PatchOperation.Set("/updatedAt",  DateTimeOffset.UtcNow),
        };
        try
        {
            var r = await _container.PatchItemAsync<Ticket>(id, new PartitionKey(id), patches, cancellationToken: ct);
            return r.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Ticket?> AssignTicketAsync(
        string id, string assignTo, CancellationToken ct = default)
    {
        // Read first to conditionally advance status Open → InProgress
        var ticket = await GetByIdAsync(id, ct);
        if (ticket is null) return null;

        var patches = new List<PatchOperation>
        {
            PatchOperation.Set("/assignedTo", assignTo),
            PatchOperation.Set("/updatedAt",  DateTimeOffset.UtcNow),
        };
        if (ticket.Status == TicketStatus.Open)
            patches.Add(PatchOperation.Set("/status", TicketStatus.InProgress.ToString()));

        var r = await _container.PatchItemAsync<Ticket>(id, new PartitionKey(id), patches, cancellationToken: ct);
        return r.Resource;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> ScalarQueryAsync<T>(string sql, CancellationToken ct)
    {
        using var iter = _container.GetItemQueryIterator<T>(new QueryDefinition(sql));
        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(ct);
            var val  = page.FirstOrDefault();
            if (val is not null) return val;
        }
        return default!;
    }

    // ── Seed Data ─────────────────────────────────────────────────────────────

    private async Task SeedAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        async Task MakeAsync(
            string title, string desc,
            TicketPriority prio, TicketCategory cat,
            string user, TicketStatus status, TimeSpan age,
            string? assignedTo = null, string? resolution = null,
            List<TicketComment>? comments = null)
        {
            var seq       = Interlocked.Increment(ref _nextId);
            var id        = $"INC-{seq}";
            var createdAt = now - age;
            var ticket    = new Ticket
            {
                Id          = id,
                Seq         = seq,
                Title       = title,
                Description = desc,
                Priority    = prio,
                Category    = cat,
                RequestedBy = user,
                Status      = status,
                AssignedTo  = assignedTo,
                Resolution  = resolution,
                CreatedAt   = createdAt,
                UpdatedAt   = createdAt + TimeSpan.FromMinutes(30),
                Comments    = comments ?? [],
            };
            await _container.CreateItemAsync(ticket, new PartitionKey(id), cancellationToken: ct);
        }

        await MakeAsync("VPN keeps disconnecting after Windows update",
             "After last Tuesday's Windows Update, Cisco AnyConnect disconnects every 20-30 minutes. Tried restarting — issue persists. Error: 'Secure gateway has rejected the connection attempt.'",
             TicketPriority.High, TicketCategory.VPN, "alice@contoso.com",
             TicketStatus.InProgress, TimeSpan.FromHours(5),
             assignedTo: "helpdesk-tier2@contoso.com",
             comments:
             [
                 new() { Author = "helpdesk-tier2@contoso.com", Message = "Reproduced on AnyConnect 4.10. Likely KB5034441 regression. Rolling out AnyConnect 5.1 hotfix — will update.", IsInternal = true },
                 new() { Author = "alice@contoso.com",          Message = "Still happening as of this morning. Disrupting calls.", IsInternal = false },
             ]);

        await MakeAsync("Cannot open Outlook — OST profile error",
             "Outlook throws 'Cannot open your default email folders. The file outlook.ost is not an Outlook data file.' on every startup. Tried safe mode — same error.",
             TicketPriority.High, TicketCategory.Email, "bob@contoso.com",
             TicketStatus.InProgress, TimeSpan.FromHours(3),
             assignedTo: "helpdesk-tier1@contoso.com",
             comments:
             [
                 new() { Author = "helpdesk-tier1@contoso.com", Message = "Guided user to rename .ost file. Rebuilding mailbox cache — ETA 30 mins.", IsInternal = false },
             ]);

        await MakeAsync("Request access to Finance SharePoint Q4 site",
             "Need read access to the Finance Q4 reporting SharePoint site for the quarterly review. Manager Carol Smith has approved via email (attached).",
             TicketPriority.Medium, TicketCategory.Access, "dave@contoso.com",
             TicketStatus.PendingUser, TimeSpan.FromHours(26),
             assignedTo: "helpdesk-tier1@contoso.com",
             comments:
             [
                 new() { Author = "helpdesk-tier1@contoso.com", Message = "Please confirm your manager's email address so we can verify the approval.", IsInternal = false },
             ]);

        await MakeAsync("Laptop screen flickering — Dell XPS 15",
             "Screen flickers every 5-10 seconds with horizontal lines. Happens on AC and battery. External monitor works fine. Driver update didn't help.",
             TicketPriority.Medium, TicketCategory.Hardware, "priya@contoso.com",
             TicketStatus.Open, TimeSpan.FromHours(1));

        await MakeAsync("Cannot install Docker Desktop — permission denied",
             "Getting 'Access is denied' when installing Docker Desktop 4.29. Running as local admin. Group Policy may be blocking Hyper-V.",
             TicketPriority.Medium, TicketCategory.Software, "rahul@contoso.com",
             TicketStatus.Open, TimeSpan.FromMinutes(45));

        await MakeAsync("MFA token not working after phone replacement",
             "Got a new iPhone and the Microsoft Authenticator codes are rejected. Cannot log in to any M365 service. Locked out completely.",
             TicketPriority.Critical, TicketCategory.Access, "sara@contoso.com",
             TicketStatus.InProgress, TimeSpan.FromMinutes(90),
             assignedTo: "helpdesk-tier1@contoso.com",
             comments:
             [
                 new() { Author = "helpdesk-tier1@contoso.com", Message = "Identity verified via employee ID. Resetting MFA registration — user to re-enrol via aka.ms/mfasetup.", IsInternal = false },
                 new() { Author = "helpdesk-tier1@contoso.com", Message = "Temp access code issued. Monitoring re-enrolment.", IsInternal = true },
             ]);

        await MakeAsync("Shared mailbox not appearing in Outlook",
             "The 'IT-Alerts@contoso.com' shared mailbox was added to my account last week but never appeared in Outlook. Checked Online Archive — not there either.",
             TicketPriority.Low, TicketCategory.Email, "mike@contoso.com",
             TicketStatus.Open, TimeSpan.FromHours(2));

        await MakeAsync("Azure DevOps pipeline failing — agent offline",
             "Build pipeline for the 'HelpdeskAI' repo has been failing since 09:00 with 'No agent found in pool Default'. Other pipelines in the same pool also affected.",
             TicketPriority.High, TicketCategory.Software, "alex.johnson@contoso.com",
             TicketStatus.InProgress, TimeSpan.FromMinutes(30),
             assignedTo: "devops-team@contoso.com",
             comments:
             [
                 new() { Author = "devops-team@contoso.com", Message = "Known issue — scheduled maintenance on agent pool. Agents back online by 13:00 UTC.", IsInternal = false },
             ]);

        await MakeAsync("Wi-Fi dropping every hour in Building C",
             "Multiple users in Building C (3rd floor, near conf rooms 3A-3D) report Wi-Fi dropping for 2-3 minutes every hour. Happens on CONTOSO-5G and CONTOSO-2G.",
             TicketPriority.High, TicketCategory.Network, "facilities@contoso.com",
             TicketStatus.InProgress, TimeSpan.FromHours(4),
             assignedTo: "netops@contoso.com",
             comments:
             [
                 new() { Author = "netops@contoso.com", Message = "Channel interference detected on AP-3C-04. Reconfigured to channel 149. Monitoring for 2 hours.", IsInternal = true },
                 new() { Author = "netops@contoso.com", Message = "Issue appears resolved post-reconfiguration. Keeping ticket open for monitoring.", IsInternal = false },
             ]);

        await MakeAsync("Request new MacBook Pro for onboarding — James Patel",
             "New hire James Patel (Engineering, start date next Monday) needs a MacBook Pro 14\" M3 with standard dev setup. Asset request approved by dept head.",
             TicketPriority.Medium, TicketCategory.Hardware, "hr@contoso.com",
             TicketStatus.Open, TimeSpan.FromHours(8));

        await MakeAsync("Password reset — locked out of Windows",
             "Locked out after too many failed attempts on Monday morning.",
             TicketPriority.High, TicketCategory.Access, "carol@contoso.com",
             TicketStatus.Resolved, TimeSpan.FromDays(2),
             assignedTo: "helpdesk-tier1@contoso.com",
             resolution: "Password reset via SSPR. User re-authenticated successfully. Advised to use password manager.",
             comments:
             [
                 new() { Author = "helpdesk-tier1@contoso.com", Message = "Reset completed. User confirmed access restored.", IsInternal = false },
             ]);

        await MakeAsync("Slow internet — only 2 Mbps on office ethernet",
             "Ethernet speed dropped to 2 Mbps. Wi-Fi fine at 200+ Mbps. Tried two cables and two ports.",
             TicketPriority.Medium, TicketCategory.Network, "tom@contoso.com",
             TicketStatus.Resolved, TimeSpan.FromDays(1),
             assignedTo: "netops@contoso.com",
             resolution: "Faulty patch panel port replaced on Floor 2 switch cabinet. Speed confirmed at 1 Gbps.",
             comments:
             [
                 new() { Author = "netops@contoso.com", Message = "Port 24 on SW-F2-01 was degraded. Moved to port 26. Speed test: 980 Mbps. Issue resolved.", IsInternal = false },
             ]);

        await MakeAsync("Company Portal not loading on Mac — stuck on spinner",
             "Company Portal app shows infinite spinner on macOS. Cannot install approved software or check compliance status.",
             TicketPriority.Low, TicketCategory.Software, "nina@contoso.com",
             TicketStatus.Resolved, TimeSpan.FromDays(3),
             assignedTo: "helpdesk-tier1@contoso.com",
             resolution: "Cleared Intune MDM enrollment cache and re-enrolled device. Company Portal loaded normally after re-enrolment.",
             comments:
             [
                 new() { Author = "helpdesk-tier1@contoso.com", Message = "Ran: sudo profiles remove -all, then re-enrolled via aka.ms/mdmwindows equivalent for Mac. Resolved.", IsInternal = true },
                 new() { Author = "nina@contoso.com",            Message = "Working now, thank you!", IsInternal = false },
             ]);
    }
}
