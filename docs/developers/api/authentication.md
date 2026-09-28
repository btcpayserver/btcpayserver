# API authentication and authorization

Greenfield endpoints support API keys and, where indicated, HTTP Basic authentication. API keys are the normal integration credential because they can be restricted by permission and store.

## API keys

Send an API key with the nonstandard `token` authorization scheme:

```http
Authorization: token YOUR_API_KEY
```

The API reference shows the permission required by each operation. A store permission can be unscoped, such as `btcpay.store.cancreateinvoice`, or restricted to a store by appending its ID, such as `btcpay.store.cancreateinvoice:STORE_ID`.

Request the narrowest useful permissions. Omitting permissions when creating a key can produce unrestricted access, so do not rely on an empty list to mean no access.

Users can create keys under **Account > Manage account > API keys**. A user can also call the create API key endpoint using Basic authentication or an unrestricted key. Server administrators can create a key for another user through the administrator endpoint. Consult the target instance's `/docs` for the exact operations and request models.

## Basic authentication

Basic authentication sends the user's email and password and gives the request the user's unrestricted access. By default, it is available only during the first five minutes after the account is created so the user can bootstrap an API key. The user can enable it indefinitely from their account profile with **Allow Basic authentication for Greenfield API**.

Basic authentication is rate limited and intended mainly to bootstrap an API key. Do not ask users to disclose their password to a third-party application and do not retain it as an integration credential.

## Interactive authorization

An integration needs an API key belonging to a BTCPay Server user before it can act on that user's behalf, but it should not ask the user to copy a key or disclose account credentials. Interactive authorization solves this by letting the integration request only the permissions it needs and redirect the user to their own BTCPay Server instance to review and approve the request. After approval, BTCPay Server creates or reuses a suitable API key and sends the user back to the integration's callback with the key and its granted permissions in a form POST.

Third-party applications should redirect the user to `/api-keys/authorize` on the user's own instance. The application can prefill:

- `applicationName`, shown to the user.
- One or more `permissions` values.
- `selectiveStores=true`, allowing the user to scope store permissions.
- `strict=true`, preventing changes to the requested permission list.
- `redirect`, an HTTPS callback that receives a form POST containing `apiKey`, `userId`, and repeated `permissions[]` fields.
- `applicationIdentifier`, used with `redirect` to recognize a prior authorization for the same application, redirect host, and permissions.

Example:

```text
https://your-btcpay.example/api-keys/authorize?applicationName=ExampleApp&permissions=btcpay.store.cancreateinvoice&permissions=btcpay.store.canviewinvoices&selectiveStores=true&strict=true&redirect=https%3A%2F%2Fapp.example%2Fbtcpay%2Fcallback&applicationIdentifier=example-app
```

Build this URL with a URL encoder. Validate that the user-provided instance URL is HTTPS, include unguessable state in the callback URL and bind it to the initiating session, and verify the returned permissions and store scope before saving the key. Never send the key onward in a query string or log it.

The operation and callback schema are documented under **Authorization** in the instance's `/docs` or the [hosted API reference](https://docs.btcpayserver.org/API/Greenfield/v1/#tag/Authorization).
