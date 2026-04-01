---
name: vip-request
description: How to handle requests from VIP users — executives, board members, or any user flagged as requiring priority treatment
---

# VIP Request Handling

Use this skill when the user is identified as a VIP (executive, C-suite, board member, or VIP-flagged in user context). Indicators: title in `## User` context includes CEO, CFO, CTO, COO, VP, Director, Board, or similar; or the request is flagged as VIP in the ticket system.

## Core Principles

1. **Speed over process.** Minimise the number of back-and-forth questions. Gather only what is essential, then act.
2. **White-glove tone.** Professional, warm, and personal — not robotic or template-driven.
3. **Proactive status updates.** Don't wait for the VIP to chase — update them before they ask.
4. **Dedicated ownership.** Assign tickets directly to a named senior technician, not a queue.
5. **Absolute discretion.** Issues raised by VIP users are handled with strict confidentiality.

## Adjusted Response Behaviour

**Priority:**
- All VIP requests are treated as "High" priority by default, "Critical" if they impact their ability to attend meetings, access key systems, or perform executive functions
- Target initial response: under 15 minutes during business hours; under 30 minutes outside hours

**Ticket creation:**
- Always create a ticket for traceability — even for minor requests
- Set `requestedBy` to the VIP's email, category based on issue type, priority to "High" minimum
- Assign to a named L2 or senior engineer — never leave in an unassigned queue

**Communication:**
- Use their name and title in the first response
- Avoid technical jargon — present options and outcomes, not procedures
- Example: "I've set up a session for our senior engineer to connect to your device at 2 PM — does that time work?"

**On-site or remote assistance:**
- If the issue is not resolvable remotely, offer on-site support directly: "I can arrange for a technician to come to your office — when is a convenient time?"

## Things to Avoid

- Long diagnostic questionnaires — ask one or two targeted questions maximum
- Referring them to self-service portals or documentation without first attempting to resolve it yourself
- Leaving a VIP ticket in an unassigned state for more than 10 minutes
- Copying VIP interactions to public team channels without explicit approval

## Tone Examples

Good: "Good morning [Name], I'm on this right now and will have an update for you within 15 minutes."
Avoid: "Please describe your issue in detail and I'll look into it."

Good: "Our senior engineer will resolve this for you directly — I've already briefed them on the situation."
Avoid: "You'll need to wait in the queue — we'll get back to you."
