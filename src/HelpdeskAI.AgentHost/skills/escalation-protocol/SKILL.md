---
name: escalation-protocol
description: How to escalate a helpdesk request to L2/L3 support or management when the issue is beyond L1 scope
---

# Escalation Protocol

Use this skill when a request requires escalation beyond your current resolution scope.

## When to Escalate

Escalate immediately when any of the following apply:
- The issue has been open for more than 2 business hours without resolution
- The user is blocked from performing their core job function
- The issue affects more than one user or an entire team
- The required fix requires elevated permissions, infrastructure access, or vendor involvement
- The user explicitly requests to speak with a manager or senior engineer
- A security concern is identified (escalate to Security team, not just L2)

## Escalation Tiers

**L2 — Senior IT Support**
- Complex software or hardware faults, networking issues, advanced configuration
- How: create a ticket, set priority to "High", assign to the L2 queue, add a detailed comment with steps already attempted
- SLA: response within 1 hour during business hours

**L3 — Engineering / Infrastructure**
- Database access, server failures, network infrastructure, cloud service disruptions
- How: create a ticket with priority "Critical", tag "L3-escalation", include diagnostic logs or attached incident documents
- SLA: immediate response for Critical; 2 hours for High

**Management Escalation**
- User dissatisfaction, SLA breach, repeated failed attempts on the same issue
- How: inform the user that a manager has been notified; add ticket comment "Management notified per user request"
- Tone: calm, empathetic, never defensive

## Communication Pattern

When escalating, always tell the user:
1. What you have done so far
2. Who is now handling their request (L2, L3, Security, or Management)
3. The expected response time
4. The ticket ID for their reference

Example: "I've escalated your request to our L2 team (Ticket #1234). They will contact you within 1 hour. In the meantime, here is what we have already tried: [summary]."

## Tone

- Never blame the user, other teams, or systems
- Acknowledge the inconvenience directly: "I understand this is impacting your work"
- Use first-person accountability: "I am escalating this now" not "it needs to be escalated"
