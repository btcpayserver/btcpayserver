---
name: btcpayserver-pr-descriptions
description: Use when writing, reviewing, or improving pull request descriptions for BTCPayServer. Focuses on non-technical, user-centered descriptions and useful visual evidence.
---

# BTCPayServer Pull Request Descriptions

Write pull request descriptions for the people who need to understand the change, not for people who can already read the diff.

## Audience

- Assume most readers are non-technical users, merchants, admins, support people, translators, or reviewers trying to understand the product impact.
- Use technical details only when the pull request is itself technical and the details are necessary to review or explain the change.
- Avoid implementation jargon such as controller, view model, migration, refactor, endpoint, dependency injection, database schema, or renamed class unless that is the actual user-facing concern.

## Content

- Explain what changed in terms of user-visible behavior, workflows, screens, settings, permissions, API behavior, or operational impact.
- Describe why the change matters when it is not obvious from the title.
- Include the practical before-and-after effect when relevant.
- Mention limitations, compatibility concerns, or follow-up work if users or operators should know about them.
- Do not repeat a file-by-file or commit-by-commit summary that reviewers can already see in the diff.
- Do not describe purely internal implementation choices unless they affect how someone uses, deploys, reviews, or tests BTCPay Server.

## Visual Evidence

- Prefer screenshots for visual changes.
- Prefer a short video or GIF for flows, animations, checkout behavior, Point of Sale behavior, or changes that require several steps to understand.
- Include visuals for UI changes unless the change is too small, invisible, or impractical to capture.
- If visuals are omitted for a visual change, briefly explain why.

## Suggested Structure

- Start with a short paragraph explaining the functional change in plain language.
- Add screenshots or a short video when applicable.
- Add a concise testing note that describes the user flow or scenario checked, not just the command that was run.
- Add technical notes only when they are necessary for reviewers, operators, integrators, or plugin authors.

## Style

- Be concise and concrete.
- Prefer plain language over product-internal terminology.
- Keep the description focused on outcomes and behavior.
- Avoid filler such as "this PR updates files" or "this PR changes logic".
- Avoid overstating the impact; say what changed and who benefits.
