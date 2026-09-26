# Plugin authentication and permissions

Protect every plugin route explicitly. UI and API routes use different authentication schemes even when they enforce the same BTCPay policy.

## Existing policies

For an HTML controller, use cookie authentication and an existing policy where it describes the operation:

```csharp
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie,
    Policy = Policies.CanViewProfile)]
public sealed class ExampleController : Controller
{
}
```

For a Greenfield-style API controller, use `AuthenticationSchemes.Greenfield`. This accepts the Greenfield API-key and Basic handlers; normal callers should use keys.

Conditional UI is not authorization. Permission tag helpers can hide controls, but the controller action must enforce the policy independently.

## Custom policies

Register a `PolicyDefinition` only when existing policies do not fit. Plugin policy names must begin with `btcpay.` and should use one of these prefixes:

- `btcpay.plugin.store` for store-scoped access.
- `btcpay.plugin.server` for server-administrator access.
- `btcpay.plugin.user` for user-level access.

```csharp
services.AddPolicyDefinitions(new PolicyDefinition(
    "btcpay.plugin.store.example.canview",
    new PermissionDisplay("View example data", "Allows viewing example data in all stores."),
    new PermissionDisplay("View example data", "Allows viewing example data in selected stores.")));
```

A permission may be unscoped or suffixed with `:STORE_ID`. Policies ending in `:` require an unscoped permission; use that form only for operations such as creating a store where a specific existing store cannot provide the scope.

For store policies, the built-in scope provider resolves common route values such as `storeId`, `appId`, `invoiceId`, and `payReqId`. A plugin can register a `BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery` for another route value. Implement `IPermissionScopeProvider` or `IPermissionHandler` only when the built-in store/server model cannot represent the resource.

After successful store authorization, use the `HttpContext` store/resource helpers populated by the authorization handler rather than loading an unrelated store from an untrusted route value.

See [`PolicyDefinition`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer/Services/PolicyDefinition.cs) and [`BuiltInPermissionScopeProvider`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer/Security/BuiltInPermissionScopeProvider.cs) for the current framework contract.
