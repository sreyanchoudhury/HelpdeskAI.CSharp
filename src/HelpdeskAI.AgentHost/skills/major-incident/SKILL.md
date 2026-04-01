---
name: major-incident
description: How to respond and coordinate during a major incident — a service outage or degradation affecting multiple users or a business-critical system
---

# Major Incident Response

Use this skill when an issue is confirmed or suspected to affect multiple users, a whole team, or a critical business system. Indicators: multiple users reporting the same issue simultaneously, a system health check shows active incidents, or a user describes "everyone on my team is affected."

## What Is a Major Incident

A major incident (P1/P2) is any unplanned interruption or quality degradation of a critical IT service that:
- Affects 5 or more users, or an entire business unit
- Blocks a revenue-generating, customer-facing, or safety-critical process
- Is not resolvable by standard L1 troubleshooting within 15 minutes

## Immediate Actions (First 5 Minutes)

1. **Confirm scope** — ask: "Is this affecting just you, or others on your team as well?"
2. **Check system status** — use `get_system_status` and `get_active_incidents` to see if an incident is already declared
3. **If no incident is declared yet** — create a ticket with priority "Critical" and clearly describe the business impact
4. **Notify the user of status** — tell them an incident has been identified and the team is aware

## Communication During the Incident

- Provide a status update at least every 30 minutes during business hours
- Use factual language: "We have identified the root cause and are applying a fix" not "hopefully it will be resolved soon"
- Never speculate about timelines unless engineering has confirmed an ETA
- Always include the ticket/incident ID in every communication

## Coordinating Across Teams

- Route to `incident_agent` to check and correlate impact across departments
- If the incident is infrastructure-related, escalate to L3 immediately
- If a vendor dependency is involved, note it in the ticket and flag for account management

## Post-Incident (After Resolution)

Once the incident is resolved:
1. Confirm with the affected user(s) that service is restored
2. Update ticket status to "Resolved" with a resolution summary
3. Suggest indexing the resolution to the KB for future reference
4. Briefly mention if a post-incident review (PIR) will be conducted for P1 incidents

## Tone During Major Incidents

- Calm and structured — users are already stressed
- Communicate certainty where you have it, and honest uncertainty where you do not
- Avoid corporate language like "we regret any inconvenience caused" — be direct and human
