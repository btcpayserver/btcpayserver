# Agent Instructions

Repository-specific agent guidance has moved to project skills:

- `.agents/skills/btcpayserver-migrations/SKILL.md`
- `.agents/skills/btcpayserver-changelog/SKILL.md`
- `.agents/skills/btcpayserver-pr-descriptions/SKILL.md`
- `.agents/skills/btcpayserver-configuration/SKILL.md`
- `.agents/skills/playwright-test-patterns/SKILL.md`

Load the relevant skill when creating migrations, updating/reviewing `Changelog.md`, writing/reviewing pull request descriptions, adding/reviewing startup configuration options, or writing/refactoring Playwright tests.

## JSON Serialization

Prefer `Newtonsoft.Json` over `System.Text.Json` when adding or modifying JSON serialization code.
