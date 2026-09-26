---
name: btcpayserver-pr-descriptions
description: Use when writing, reviewing, or improving pull request descriptions for BTCPayServer. Focuses on non-technical, user-centered descriptions and useful visual evidence.
---

# BTCPayServer Pull Request Descriptions

Follow [Coding conventions](../../../docs/maintainers/README.md#pull-requests) and [API changes](../../../docs/maintainers/README.md#api-changes). Draft the description from the actual diff and repository context; do not infer product impact from the title alone.

## GitHub CLI Formatting

- When creating or editing PR descriptions with `gh pr create` or `gh pr edit`, pass real multiline Markdown so GitHub renders paragraphs, lists, and code blocks correctly.
- Do not pass literal `\n` sequences in quoted strings. Use a heredoc, a temporary body file, or Bash ANSI-C quoting (`$'...'`) when invoking `gh` from the shell.
