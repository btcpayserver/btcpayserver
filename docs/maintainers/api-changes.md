# API Changes

The Greenfield API contract includes controller behavior, models, permissions, serialization, and the hand-maintained OpenAPI templates in `BTCPayServer/wwwroot/swagger/v1/`.

## New Endpoints

- Document every endpoint and schema in the matching `swagger.template.*.json` file.
- Assign the correct permission; introduce a permission only when no existing one fits.
- Use REST methods where practical: `POST` for creation or actions, `PUT` for full replacement, `PATCH` for partial updates, and `DELETE` for deletion or archival.
- Return validation failures as HTTP 422 with `path` and `message` entries. Return business request failures as HTTP 400 with a stable `code` and human-readable `message`.
- Register JSON converters on the model with attributes. Serialize precision-sensitive or overflow-prone values such as `decimal` and `long` as strings while accepting compatible input forms where required.

## Compatibility

Changing a property type or removing a property is breaking; version the endpoint unless compatibility can be preserved completely. Adding a required property or one without a safe default can also break clients. For additions, detect omission and retain the existing value on updates or apply a documented default on creation.

Update the matching OpenAPI template in the same pull request whenever request fields, response fields, validation, models, or behavior change. Cover compatibility and permissions with Greenfield API tests.

See [Greenfield API development](../greenfield-development.md) for detailed model-evolution examples and [authorization](../greenfield-authorization.md) for authentication flows.
