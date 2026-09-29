---
name: bem-conventions
description: Use when editing Razor views, view components, CSS, JavaScript DOM selectors, or Playwright tests that depend on frontend selectors. Prefer BEM class selectors over ids for reusable UI hooks.
---

# BEM Conventions

Follow [Coding conventions](../../../docs/maintainers/coding-conventions.md#frontend-selectors).

## Refactoring Existing Code

- Add BEM classes before changing tests or scripts.
- Update CSS, JavaScript, and Playwright selectors to use the BEM classes.
- Preserve existing ids unless there is a clear reason they are safe to remove.
- Run the relevant build or tests after selector changes.
