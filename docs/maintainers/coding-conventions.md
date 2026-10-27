# Coding Conventions

## General

- Follow the repository `.editorconfig`; it is the source of truth for
  formatting and C# style.
- Prefer expression-bodied methods when the implementation fits on one line.
- Prefer `Newtonsoft.Json` over `System.Text.Json` when adding or changing JSON
  serialization.
- Use `JsonParser` instead of `JObject.Parse`, `JToken.Parse`, or `JArray.Parse`.
  It preserves date-like strings and enforces an explicit maximum depth. Pass a
  different depth only when the JSON envelope is intentionally deeper than the
  default limit.
- Do not use `ConfigureAwait(false)`; BTCPay Server does not rely on a
  synchronization context.

## Logging and Exceptions

- Use `ILogger` only for messages that are relevant to server administrators.
  As a rule of thumb, a malicious actor should not be able to spam the logs by
  repeatedly triggering application behavior.
- Treat internal exception messages as safe to expose to end users for
  diagnostic purposes unless there is a specific reason to believe they contain
  sensitive data.

## PostgreSQL Schema Naming

Use PostgreSQL-style physical names for new tables while keeping normal .NET
naming in the entity classes:

- Use lowercase, plural `snake_case` table names, such as `email_rules`.
- Use lowercase `snake_case` column names, including foreign keys and timestamps,
  such as `store_id` and `created_at`.
- Map CLR names explicitly with `[Table]`, `[Column]`, and related attributes
  whenever possible. Use fluent configuration only for mappings that attributes
  cannot express. Do not rely on EF's default PascalCase names for new schema
  objects.
- Use lowercase PostgreSQL type names such as `text`, `jsonb`, `text[]`, and
  `timestamptz` when specifying a database type explicitly.
- Let EF derive primary key, foreign key, and index names from the physical
  table and column names unless a stable custom name is required.
- Use unquoted lowercase identifiers in raw SQL for new schema objects. Quote
  existing mixed-case identifiers when referencing legacy tables or columns.

These rules apply to new schema objects. Do not rename established tables or
columns solely for consistency; preserve compatibility with existing databases
and integrations.

## Pull Requests

Write descriptions for users, merchants, operators, support contributors,
translators, and reviewers who need to understand the outcome rather than the
implementation.

- Explain user-visible behavior, workflows, settings, permissions, API
  behavior, and operational impact in plain language.
- State why the change matters and describe the practical before-and-after
  effect when useful.
- Mention limitations, compatibility concerns, and follow-up work that affects
  users or operators.
- Do not repeat the diff or include routine verification commands.
- Keep technical implementation details only when they are necessary for
  review or explain public behavior.
- Add screenshots for visual changes and a short video or GIF for multi-step UI
  flows when practical. Briefly explain when useful visual evidence cannot be
  included.

## Frontend Selectors

Use BEM-style classes for reusable styling, JavaScript, and Playwright hooks:
`.block`, `.block__element`, `.block--modifier`, and
`.block__element--modifier`. Use the component name as the block. Scope DOM
queries to the nearest component or form when possible.

Keep ids required for labels, ARIA and Bootstrap wiring, browser behavior,
model binding, or compatibility. Even when an id remains, use a BEM class for
new component selectors.

## Razor Localization

- Use `StringLocalizer` for plain text; Razor encodes the localized result.
- Use `ViewLocalizer` only when the resource intentionally contains HTML.
- Pass dynamic `ViewLocalizer` parameters through `Html.Encode(...)`.
- Do not encode intentional HTML returned by helpers such as
  `Html.ActionLink(...)`.

## Changelog

Do not edit `Changelog.md` in feature, fix, refactor, or documentation pull
requests merely because a change is user-visible. Only update it when
changelog or release-note maintenance is an explicit motivation of the pull
request.

When maintaining the changelog, record user-visible features, fixes,
regressions, deprecations, removals, security-relevant behavior, and
compatibility changes. Skip internal refactors, test-only changes, tooling
changes unless users or release operators are affected, and entries already
covered by an earlier patch release. Put removals and deprecations under
**Miscellaneous** unless another existing section is a better fit.

Use concise imperative bullets under the existing sections, preserve product
terminology, wrap identifiers in backticks, and include PR numbers and
contributor handles when known. When an entry begins with a titled prefix, bold
only that title: `* **Title**: Description`.
