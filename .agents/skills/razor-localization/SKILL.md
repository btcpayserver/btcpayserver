---
name: razor-localization
description: Use when editing or reviewing Razor `.cshtml` files containing parameterized localized strings. Choose StringLocalizer for plain text and safely encode ViewLocalizer parameters when localized strings contain HTML.
---

# Razor Localization

Apply these rules to parameterized localizable strings in Razor views.

## Plain Text

Use `StringLocalizer` when the localized string does not contain HTML. Razor encodes the resulting localized string when rendering it.

```razor
@StringLocalizer["{0} has been invited as {1}.", Model.Email, Model.Role]
```

Do not use `ViewLocalizer` merely because a string has parameters.

## HTML

Use `ViewLocalizer` only when the localized string intentionally contains HTML. Encode every dynamic parameter with `Html.Encode` before passing it to `ViewLocalizer`.

```razor
@ViewLocalizer["You have been invited to join <strong>{0}</strong> as {1}.",
    Html.Encode(Model.StoreName), Html.Encode(Model.Role)]
```

Never pass user-controlled or otherwise dynamic strings directly to `ViewLocalizer`:

```razor
@* Unsafe *@
@ViewLocalizer["Welcome to <strong>{0}</strong>.", Model.StoreName]
```

Generated HTML values such as `Html.ActionLink(...)` are intentional HTML and should not be encoded.

## Review Checklist

- Use `StringLocalizer` for parameterized strings without HTML.
- Use `ViewLocalizer` only when the localized resource contains intentional HTML.
- Wrap every dynamic `ViewLocalizer` parameter in `Html.Encode(...)`.
- Do not encode intentional HTML values returned by HTML helpers.
- Check every added or modified `ViewLocalizer` call before completing a Razor change.
