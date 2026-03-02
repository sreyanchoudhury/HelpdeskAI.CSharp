using System.Collections.Concurrent;
using HelpdeskAI.McpServer.Models;

namespace HelpdeskAI.McpServer.Services;

public sealed class TicketService
{
	private readonly ConcurrentDictionary<string, Ticket> _tickets = new();
	private int _nextId = 1000;

	public TicketService() => Seed();

	public Ticket CreateTicket(string title, string description, TicketPriority priority, TicketCategory category, string requestedBy)
	{
		var id = $"INC-{Interlocked.Increment(ref _nextId)}";
		var ticket = new Ticket
		{
			Id = id,
			Title = title,
			Description = description,
			Priority = priority,
			Category = category,
			RequestedBy = requestedBy
		};
		_tickets[id] = ticket;
		return ticket;
	}

	public Ticket? GetById(string id) => _tickets.GetValueOrDefault(id);

	public IReadOnlyList<Ticket> Search(string? requestedBy, TicketStatus? status, TicketCategory? category) =>
		_tickets.Values
			.Where(t => requestedBy is null || t.RequestedBy.Contains(requestedBy, StringComparison.OrdinalIgnoreCase))
			.Where(t => status is null || t.Status == status)
			.Where(t => category is null || t.Category == category)
			.OrderByDescending(t => t.CreatedAt)
			.Take(15)
			.ToList();

	public Ticket? UpdateStatus(string id, TicketStatus status, string? resolution)
	{
		if (!_tickets.TryGetValue(id, out var ticket)) return null;
		ticket.Status = status;
		ticket.UpdatedAt = DateTimeOffset.UtcNow;
		if (resolution is not null) ticket.Resolution = resolution;
		return ticket;
	}

	public Ticket? AddComment(string id, string author, string message, bool isInternal)
	{
		if (!_tickets.TryGetValue(id, out var ticket)) return null;
		ticket.Comments.Add(new TicketComment { Author = author, Message = message, IsInternal = isInternal });
		ticket.UpdatedAt = DateTimeOffset.UtcNow;
		return ticket;
	}

	private void Seed()
	{
		var now = DateTimeOffset.UtcNow;

		Ticket Make(string title, string desc, TicketPriority prio, TicketCategory cat,
											string user, TicketStatus status, TimeSpan age,
											string? assignedTo = null, string? resolution = null,
											List<TicketComment>? comments = null)
		{
			var createdAt = now - age;
			var updatedAt = createdAt + TimeSpan.FromMinutes(30);
			var t = new Ticket
			{
				Id = $"INC-{Interlocked.Increment(ref _nextId)}",
				Title = title,
				Description = desc,
				Priority = prio,
				Category = cat,
				RequestedBy = user,
				Status = status,
				AssignedTo = assignedTo,
				Resolution = resolution,
				CreatedAt = createdAt,
				UpdatedAt = updatedAt,
				Comments = comments ?? new List<TicketComment>()
			};
			_tickets[t.Id] = t;
			return t;
		}

	

		Make("VPN keeps disconnecting after Windows update",
			 "After last Tuesday's Windows Update, Cisco AnyConnect disconnects every 20-30 minutes. Tried restarting  issue persists. Error: 'Secure gateway has rejected the connection attempt.'",
			 TicketPriority.High, TicketCategory.VPN, "alice@contoso.com",
			 TicketStatus.InProgress, TimeSpan.FromHours(5),
			 assignedTo: "helpdesk-tier2@contoso.com",
			 comments:
			 [
				 new() { Author = "helpdesk-tier2@contoso.com", Message = "Reproduced on AnyConnect 4.10. Likely KB5034441 regression. Rolling out AnyConnect 5.1 hotfix  will update.", IsInternal = true },
				 new() { Author = "alice@contoso.com",          Message = "Still happening as of this morning. Disrupting calls.", IsInternal = false },
			 ]);

		Make("Cannot open Outlook  OST profile error",
			 "Outlook throws 'Cannot open your default email folders. The file outlook.ost is not an Outlook data file.' on every startup. Tried safe mode  same error.",
			 TicketPriority.High, TicketCategory.Email, "bob@contoso.com",
			 TicketStatus.InProgress, TimeSpan.FromHours(3),
			 assignedTo: "helpdesk-tier1@contoso.com",
			 comments:
			 [
				 new() { Author = "helpdesk-tier1@contoso.com", Message = "Guided user to rename .ost file. Rebuilding mailbox cache  ETA 30 mins.", IsInternal = false },
			 ]);

		Make("Request access to Finance SharePoint Q4 site",
			 "Need read access to the Finance Q4 reporting SharePoint site for the quarterly review. Manager Carol Smith has approved via email (attached).",
			 TicketPriority.Medium, TicketCategory.Access, "dave@contoso.com",
			 TicketStatus.PendingUser, TimeSpan.FromHours(26),
			 assignedTo: "helpdesk-tier1@contoso.com",
			 comments:
			 [
				 new() { Author = "helpdesk-tier1@contoso.com", Message = "Please confirm your manager's email address so we can verify the approval.", IsInternal = false },
			 ]);

		Make("Laptop screen flickering  Dell XPS 15",
			 "Screen flickers every 5-10 seconds with horizontal lines. Happens on AC and battery. External monitor works fine. Driver update didn't help.",
			 TicketPriority.Medium, TicketCategory.Hardware, "priya@contoso.com",
			 TicketStatus.Open, TimeSpan.FromHours(1));

		Make("Cannot install Docker Desktop  permission denied",
			 "Getting 'Access is denied' when installing Docker Desktop 4.29. Running as local admin. Group Policy may be blocking Hyper-V.",
			 TicketPriority.Medium, TicketCategory.Software, "rahul@contoso.com",
			 TicketStatus.Open, TimeSpan.FromMinutes(45));

		Make("MFA token not working after phone replacement",
			 "Got a new iPhone and the Microsoft Authenticator codes are rejected. Cannot log in to any M365 service. Locked out completely.",
			 TicketPriority.Critical, TicketCategory.Access, "sara@contoso.com",
			 TicketStatus.InProgress, TimeSpan.FromMinutes(90),
			 assignedTo: "helpdesk-tier1@contoso.com",
			 comments:
			 [
				 new() { Author = "helpdesk-tier1@contoso.com", Message = "Identity verified via employee ID. Resetting MFA registration  user to re-enrol via aka.ms/mfasetup.", IsInternal = false },
				 new() { Author = "helpdesk-tier1@contoso.com", Message = "Temp access code issued. Monitoring re-enrolment.", IsInternal = true },
			 ]);

		Make("Shared mailbox not appearing in Outlook",
			 "The 'IT-Alerts@contoso.com' shared mailbox was added to my account last week but never appeared in Outlook. Checked Online Archive  not there either.",
			 TicketPriority.Low, TicketCategory.Email, "mike@contoso.com",
			 TicketStatus.Open, TimeSpan.FromHours(2));

		Make("Azure DevOps pipeline failing  agent offline",
			 "Build pipeline for the 'HelpdeskAI' repo has been failing since 09:00 with 'No agent found in pool Default'. Other pipelines in the same pool also affected.",
			 TicketPriority.High, TicketCategory.Software, "alex.johnson@contoso.com",
			 TicketStatus.InProgress, TimeSpan.FromMinutes(30),
			 assignedTo: "devops-team@contoso.com",
			 comments:
			 [
				 new() { Author = "devops-team@contoso.com", Message = "Known issue  scheduled maintenance on agent pool. Agents back online by 13:00 UTC.", IsInternal = false },
			 ]);

		Make("Wi-Fi dropping every hour in Building C",
			 "Multiple users in Building C (3rd floor, near conf rooms 3A-3D) report Wi-Fi dropping for 2-3 minutes every hour. Happens on CONTOSO-5G and CONTOSO-2G.",
			 TicketPriority.High, TicketCategory.Network, "facilities@contoso.com",
			 TicketStatus.InProgress, TimeSpan.FromHours(4),
			 assignedTo: "netops@contoso.com",
			 comments:
			 [
				 new() { Author = "netops@contoso.com", Message = "Channel interference detected on AP-3C-04. Reconfigured to channel 149. Monitoring for 2 hours.", IsInternal = true },
				 new() { Author = "netops@contoso.com", Message = "Issue appears resolved post-reconfiguration. Keeping ticket open for monitoring.", IsInternal = false },
			 ]);

		Make("Request new MacBook Pro for onboarding  James Patel",
			 "New hire James Patel (Engineering, start date next Monday) needs a MacBook Pro 14\" M3 with standard dev setup. Asset request approved by dept head.",
			 TicketPriority.Medium, TicketCategory.Hardware, "hr@contoso.com",
			 TicketStatus.Open, TimeSpan.FromHours(8));

	

		Make("Password reset  locked out of Windows",
			 "Locked out after too many failed attempts on Monday morning.",
			 TicketPriority.High, TicketCategory.Access, "carol@contoso.com",
			 TicketStatus.Resolved, TimeSpan.FromDays(2),
			 assignedTo: "helpdesk-tier1@contoso.com",
			 resolution: "Password reset via SSPR. User re-authenticated successfully. Advised to use password manager.",
			 comments:
			 [
				 new() { Author = "helpdesk-tier1@contoso.com", Message = "Reset completed. User confirmed access restored.", IsInternal = false },
			 ]);

		Make("Slow internet  only 2 Mbps on office ethernet",
			 "Ethernet speed dropped to 2 Mbps. Wi-Fi fine at 200+ Mbps. Tried two cables and two ports.",
			 TicketPriority.Medium, TicketCategory.Network, "tom@contoso.com",
			 TicketStatus.Resolved, TimeSpan.FromDays(1),
			 assignedTo: "netops@contoso.com",
			 resolution: "Faulty patch panel port replaced on Floor 2 switch cabinet. Speed confirmed at 1 Gbps.",
			 comments:
			 [
				 new() { Author = "netops@contoso.com", Message = "Port 24 on SW-F2-01 was degraded. Moved to port 26. Speed test: 980 Mbps. Issue resolved.", IsInternal = false },
			 ]);

		Make("Company Portal not loading on Mac  stuck on spinner",
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

