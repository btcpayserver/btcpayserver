# Coding Conventions

## General

- Follow the repository `.editorconfig`; it is the source of truth for formatting and C# style.
- Prefer `Newtonsoft.Json` over `System.Text.Json` when adding or changing JSON serialization.

## Pull Requests

Write descriptions for users, merchants, operators, support contributors, translators, and reviewers who need to understand the outcome rather than the implementation.

- Explain user-visible behavior, workflows, settings, permissions, API behavior, and operational impact in plain language.
- State why the change matters and describe the practical before-and-after effect when useful.
- Mention limitations, compatibility concerns, and follow-up work that affects users or operators.
- Do not repeat the diff or include routine verification commands.
- Keep technical implementation details only when they are necessary for review or explain public behavior.
- Add screenshots for visual changes and a short video or GIF for multi-step UI flows when practical. Briefly explain when useful visual evidence cannot be included.

## Frontend Selectors

Use BEM-style classes for reusable styling, JavaScript, and Playwright hooks:
`.block`, `.block__element`, `.block--modifier`, and
`.block__element--modifier`. Use the component name as the block. Scope DOM
queries to the nearest component or form when possible.

Keep ids required for labels, ARIA and Bootstrap wiring, browser behavior, model binding, or compatibility. Even when an id remains, use a BEM class for new component selectors.

## Razor Localization

- Use `StringLocalizer` for plain text; Razor encodes the localized result.
- Use `ViewLocalizer` only when the resource intentionally contains HTML.
- Pass dynamic `ViewLocalizer` parameters through `Html.Encode(...)`.
- Do not encode intentional HTML returned by helpers such as `Html.ActionLink(...)`.

## Changelog

Record user-visible features, fixes, regressions, deprecations, removals, security-relevant behavior, and compatibility changes in `Changelog.md`. Skip internal refactors, test-only changes, tooling changes unless users or release operators are affected, and entries already covered by an earlier patch release. Put removals and deprecations under **Miscellaneous** unless another existing section is a better fit.

Use concise imperative bullets under the existing sections, preserve product terminology, wrap identifiers in backticks, and include PR numbers and contributor handles when known. When an entry begins with a titled prefix, bold only that title: `* **Title**: Description`.
