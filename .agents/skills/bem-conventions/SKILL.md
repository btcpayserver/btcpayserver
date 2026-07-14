---
name: bem-conventions
description: Use when editing Razor views, view components, CSS, JavaScript DOM selectors, or Playwright tests that depend on frontend selectors. Prefer BEM class selectors over ids for reusable UI hooks.
---

# BEM Conventions

Use BEM-style class names for frontend selector hooks in Razor views, view components, CSS, JavaScript, and Playwright tests.

## Default Rule

- Prefer class selectors using BEM naming: `.block`, `.block__element`, `.block--modifier`, `.block__element--modifier`.
- Use these classes for styling hooks, JavaScript DOM queries, and Playwright selectors.
- Avoid adding or depending on ids for reusable component interaction unless the id is required by platform behavior.

## When Ids Are Acceptable

Keep ids when they are needed for:

- `label for="..."` and control association.
- Bootstrap or browser wiring such as `aria-labelledby`, `data-bs-target`, modal ids, or datalist `list` targets.
- ASP.NET model binding compatibility where existing `id` or `name` values are relied on.
- Legacy compatibility when removing the id would break existing public behavior.

Even when an id must remain, add a BEM class and use the class in new CSS, JavaScript, and tests.

## View Components

For reusable components, use the component name as the BEM block:

```html
<div class="date-range-selector">
  <button class="date-range-selector__toggle">This month</button>
  <input class="date-range-selector__timezone" />
</div>
```

Examples:

- `SearchStringInput` -> `.search-string-input__text`, `.search-string-input__term`
- `DateRangeSelector` -> `.date-range-selector__toggle`, `.date-range-selector__preset`
- `ClearAllFilters` -> `.clear-all-filters__button`
- `LabelSelector` -> `.label-selector__toggle`, `.label-selector__item`

## JavaScript

- Query by BEM classes: `document.querySelector('.date-range-selector__timezone')`.
- Scope queries to the nearest component or form when possible: `element.closest('form').querySelector('.search-string-input__term')`.
- Do not use `document.getElementById(...)` for component behavior when a BEM class hook exists.

## Refactoring Existing Code

- Add BEM classes before changing tests or scripts.
- Update CSS, JavaScript, and Playwright selectors to use the BEM classes.
- Preserve existing ids unless there is a clear reason they are safe to remove.
- Run the relevant build or tests after selector changes.
