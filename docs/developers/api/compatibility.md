# API implementation and compatibility

This page is for contributors adding or changing Greenfield endpoints. Integrators should use the [getting-started guide](README.md) and the OpenAPI reference.

## Endpoint implementation

- Add the endpoint and every public schema to the manually maintained OpenAPI 3 document under `BTCPayServer/wwwroot/swagger/v1/`. The server merges those files at `/swagger/v1/swagger.json`.
- Select the correct `AuthenticationSchemes.Greenfield` authorization policy. Add a permission only when no existing policy expresses the access being granted.
- Prefer resource-oriented routes and HTTP semantics: `POST` for creation or actions, `PUT` for full replacement, `PATCH` for partial updates, and `DELETE` for deletion or archival.
- Keep JSON conversion rules on the model through attributes. Follow the repository's Newtonsoft.Json conventions.
- Represent values such as high-precision decimals and large integers as strings when JSON number precision or overflow would make clients unsafe. Accept the prior representation when compatibility requires it.
- Add endpoint tests for authorization, store scoping, validation, serialization, and documented responses.

Use `422 Unprocessable Entity` for request-model validation errors:

```json
[
  {
    "path": "propertyName",
    "message": "Human-readable message"
  }
]
```

Use `400 Bad Request` for a request that is structurally valid but cannot be completed by business logic:

```json
{
  "code": "stable-error-code",
  "message": "Human-readable message"
}
```

Keep error codes stable so clients can branch on `code` rather than parsing `message`.

## Compatibility rules

Changing a property type or removing a property is a breaking change. Version the endpoint rather than silently changing its contract. A permissive input converter does not preserve compatibility if responses still change type.

Adding an optional response property is normally safe for tolerant clients. Adding a required request property, or an optional property whose absence changes existing update behavior, can break clients. When extending a request:

- Define an explicit default for create operations.
- On updates, distinguish an omitted property from a property explicitly set to its type's default or to `null`.
- Preserve the stored value when omission means “no change.”
- Version the endpoint if old and new intent cannot be distinguished safely.

To detect omission, inspect the raw JSON object in the controller or use a Newtonsoft.Json serialization callback to record which properties were absent. Do not infer omission from the deserialized CLR default alone.

Before merging a change, compare both request and response OpenAPI schemas, generated-client behavior, authorization scope, status codes, and webhook payloads. Compatibility includes semantics, not only JSON shape.
