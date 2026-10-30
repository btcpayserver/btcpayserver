---
name: btcpayserver-pr-descriptions
description: Use when writing, reviewing, or improving pull request descriptions for BTCPayServer. Focuses on non-technical, user-centered descriptions and useful visual evidence.
---

# BTCPayServer Pull Request Descriptions

## Required context

Before writing, reviewing, or modifying a pull request description:

1. Read `docs/maintainers/coding-conventions.md`.
2. Inspect the actual diff and relevant repository context. Do not infer product impact from the PR title alone.
3. If the change affects the Greenfield API contract, also read `docs/maintainers/greenfield-api.md` before continuing.

Do not draft or modify the pull request description until the required files above have been read.

A change affects the Greenfield API contract when it changes or adds API routes, status codes, controller behavior, public request or response models, permissions, serialization, `BTCPayServerClient`, webhooks, OpenAPI definitions, or other externally observable API behavior.

## PR description requirements

Write for users, merchants, operators, support contributors, translators, and reviewers who need to understand the outcome rather than the implementation.

The description should:

- Explain user-visible behavior, workflows, settings, permissions, API behavior, or operational impact in plain language.
- Explain why the change matters.
- Describe the practical before-and-after effect when useful.
- Mention limitations, compatibility concerns, or follow-up work that affects users or operators.
- Avoid simply repeating the diff.
- Avoid routine verification commands unless they are specifically useful to the reader.
- Include technical implementation details only when they are needed for review or to explain public behavior.
- Include screenshots for visual changes.
- Include a short video or GIF for multi-step UI flows when practical.
- Briefly explain when useful visual evidence cannot be provided.

When the change affects the Greenfield API, describe relevant API-facing behavior and compatibility implications based on `docs/maintainers/greenfield-api.md`.

## GitHub CLI formatting

When creating or editing PR descriptions with `gh pr create` or `gh pr edit`:

- Pass real multiline Markdown so GitHub renders paragraphs, lists, and code blocks correctly.
- Do not pass literal `\n` sequences in quoted strings.
- Use a heredoc, a temporary body file, or Bash ANSI-C quoting (`$'...'`) when invoking `gh` from the shell.
