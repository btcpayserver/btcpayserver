---
name: razor-localization
description: Use when editing or reviewing Razor `.cshtml` files containing parameterized localized strings. Choose StringLocalizer for plain text and safely encode ViewLocalizer parameters when localized strings contain HTML.
---

# Razor Localization

Follow [Coding conventions](../../../docs/maintainers/README.md#razor-localization).

## Review Checklist

- Use `StringLocalizer` for parameterized strings without HTML.
- Use `ViewLocalizer` only when the localized resource contains intentional HTML.
- Wrap every dynamic `ViewLocalizer` parameter in `Html.Encode(...)`.
- Do not encode intentional HTML values returned by HTML helpers.
- Check every added or modified `ViewLocalizer` call before completing a Razor change.
