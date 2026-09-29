# Greenfield API Maintenance

The Greenfield API contract includes routes, status codes, controller behavior,
public models, permissions, serialization, `BTCPayServerClient`, webhooks, and
the hand-maintained OpenAPI templates in
`BTCPayServer/wwwroot/swagger/v1/`. Treat changes to semantics as carefully as
changes to JSON shape.

## Controllers and Routes

- Put core controllers in `BTCPayServer/Controllers/GreenField` and plugin
  controllers with their feature. The usual controller derives from
  `ControllerBase` and uses `[ApiController]`, Greenfield authentication, and
  `CorsPolicies.All`.
- Use explicit attribute routes under `/api/v1`. Prefer resource-oriented
  routes and use `POST` for creation or actions, `PUT` for replacement,
  `PATCH` for partial updates, and `DELETE` for deletion or archival.
- Use action path segments such as `/activate` when the operation does not map
  naturally to CRUD. Add both store-nested and resource-only routes only when
  required for compatibility; do not create aliases by default.
- Existing mutations normally return HTTP 200, including creation, deletion,
  and archival. Preserve an endpoint's established success status rather than
  changing it incidentally to 201 or 204.
- Reuse existing repositories and feature services. Keep HTTP translation,
  request validation, authorized context access, and public-model mapping at
  the controller boundary, and propagate `CancellationToken` through new
  asynchronous work.

## Authentication and Scope

- Use `AuthenticationSchemes.Greenfield` and assign the narrowest existing
  permission. Introduce a permission only when no existing policy expresses
  the access being granted. Use an unscoped permission when an operation, such
  as creating a store, cannot derive a store scope.
- Make anonymous, API-key-only, cookie-compatible, or non-`/api/v1` endpoints
  explicit exceptions. Anonymous workflows must document and test the token or
  other capability that protects the resource.
- Store authorization is derived from route, query, or form values and from
  registered resource identifiers such as `invoiceId`, `appId`, and
  `pullPaymentId`. These parameter names are security-sensitive. Register a
  `BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery` or plugin scope
  provider for a new resource identifier.
- After authorization, use context populated by the authorization
  infrastructure, such as `HttpContext.GetStoreData()`, instead of trusting a
  route value independently. When a route contains both `storeId` and a
  resource identifier, their ownership must match.
- For list endpoints, return only resources in the scopes recorded in the
  request context. Test scoped and unscoped keys, permission without store
  membership, membership without permission, cross-store access, and the
  intended 403 or 404 behavior for a missing resource.

## Public Models and Serialization

- Put reusable request and response models in
  `BTCPayServer.Client/Models`; do not return persistence or domain entities.
  Map explicitly between internal and public models.
- Register Newtonsoft.Json converters on model properties. Serialize
  precision-sensitive or overflow-prone values such as `decimal` and `long`
  as strings while accepting compatible input forms where required. Serialize
  enums as strings when that is the established contract.
- Serialize `DateTime` and `DateTimeOffset` properties as Unix timestamps with
  `NBitcoin.JsonConverters.DateTimeToUnixTimeConverter`, and document them
  with the shared `UnixTimestamp` OpenAPI schema. Preserve existing JSON names,
  time units, null handling, and defaults.

## Validation and Errors

- Add request validation failures to `ModelState` and return
  `CreateValidationError(ModelState)`. HTTP 422 responses contain an array of
  `path` and `message` entries; paths should identify nested and indexed request
  members precisely.
- Return operational failures with `CreateAPIError` and a stable error code
  plus a human-readable message. Use 400 for an otherwise valid request
  rejected by ordinary business logic, 403 for permission or policy
  restrictions, 404 for absent resources, 409 for state conflicts, 410 for
  expired or deliberately removed resources, and 503 for temporary dependency
  failures where applicable.
- Avoid bare `BadRequest()`, `NotFound()`, and `Forbid()` results. Structured
  JSON errors are required for `BTCPayServerClient` to expose useful
  `GreenfieldAPIException` details.

## Collections

The existing API has no single pagination contract. Ordinary list endpoints
usually use `skip` and `take`, while domain APIs such as Lightning use their
native cursor. For a new list endpoint, define and document stable ordering,
parameter defaults, page-size limits, invalid-value behavior, and whether the
response needs totals or continuation data. Do not change an existing raw
array response to an envelope without treating it as a compatibility change.

## OpenAPI, Client, and Tests

- Document every endpoint, route alias, parameter, request, response, schema,
  error status, and required permission in the matching
  `swagger.template.*.json` file. Use a unique `Resource_Action` operation ID.
- Update OpenAPI in the same pull request whenever request fields, response
  fields, validation, permissions, models, serialization, status codes, or
  behavior change. Schema validation does not check parity with controller
  routes or policies, so compare the merged document with the implementation.
- Add or update `BTCPayServerClient` methods with the matching HTTP method,
  request and response models, query parameters, and `CancellationToken`.
- Prefer integration tests through `BTCPayServerClient`. Cover the happy path,
  exact permission and store scope, validation status and paths, stable error
  status and code, serialization, update compatibility, pagination or filters,
  and documented responses. Extend an existing feature scenario when
  practical.

## Compatibility

Changing a property type or removing a property is breaking; version the
endpoint unless compatibility can be preserved completely. Adding a required
property or one without a safe default can also break clients. For additions,
detect omission and retain the existing value on updates or apply a documented
default on creation. Do not infer omission from the deserialized CLR default.

Historical dual routes, mixed pagination names, local response types, bare
framework errors, and direct UI-controller or database dependencies exist for
compatibility. Do not copy them into new endpoints without a concrete need.

See [API implementation and compatibility](../developers/api/compatibility.md)
for detailed model-evolution examples and
[API authentication and authorization](../developers/api/authentication.md)
for authentication flows.
