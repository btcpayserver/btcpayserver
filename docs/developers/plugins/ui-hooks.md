# UI extension points and hooks

Use an extension point or hook when core exposes one. This keeps plugins decoupled from controller overrides and copied core views.

## UI extension points

Register a partial against a named location:

```csharp
public override void Execute(IServiceCollection services)
{
    services.AddUIExtension("header-nav", "/Views/Shared/ExampleNav.cshtml");
}
```

Core renders locations with the `ui-extension-point` view component. Search the target BTCPay Server version for `vc:ui-extension-point` to find available locations and inspect the supplied model before writing the partial. Use an absolute view path to avoid accidental view-name collisions.

Common extension points include:

| Location | Model | Typical use |
|---|---|---|
| `global-nav` | `GlobalNavViewModel` | Content at the start of the global top navigation. |
| `global-nav-icons` | `GlobalNavViewModel` | Compact icon actions beside notifications and other global controls. |
| `server-nav` | `MainNavViewModel` | Server-administration navigation visible in the global settings menu. |
| `user-nav` | `MainNavViewModel` | Account-level navigation visible in the global user menu. |
| `store-nav` | `MainNavViewModel` | Store navigation entries outside a specific built-in category. |
| `store-category-nav` | `MainNavViewModel` | Entries inside the store-settings category. |
| `header-nav` | `MainNavViewModel` | General plugin entries in the main navigation's **Plugins** section. |
| `store-integrations-nav` | `MainNavViewModel` | Store-specific entries in the **Plugins** section. |
| `layout-banner` | None | A site-wide banner above the page body. |
| `dashboard` | `StoreDashboardViewModel` | Store dashboard content above the built-in widgets. |
| `checkout-end` | `CheckoutModel` | Payment-method or plugin content near the end of checkout. |

This list is intentionally not exhaustive. An extension partial must emit markup
appropriate for its location, such as an `<li>` for a navigation list. Inspect
the rendering view and a built-in registration using the same location to
confirm layout, permissions, and model assumptions for the BTCPay Server version
your plugin supports.

Embed static plugin resources through the plugin project and reference them with `~/Resources/...` plus `asp-append-version="true"`. Follow the [plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template) project settings for the current resource layout.

## Action hooks

An `IPluginHookAction` observes or performs work at a named hook and does not replace the value:

```csharp
public sealed class ExampleAction : IPluginHookAction
{
    public string Hook => "example-hook";
    public Task Execute(object args) => Task.CompletedTask;
}
```

Register it as `IPluginHookAction`. Search core for `ApplyAction(` to discover hooks and inspect the argument type at the call site.

## Filter hooks

An `IPluginHookFilter` receives the current value and returns the value passed to the next matching filter:

```csharp
public sealed class ExampleFilter : IPluginHookFilter
{
    public string Hook => "example-filter";
    public Task<object> Execute(object args) => Task.FromResult(args);
}
```

Register it as `IPluginHookFilter`. Search core for `ApplyFilter(` to discover hooks. Preserve the documented runtime type and assume other plugins may run before or after yours. Hook names are matched case-insensitively, but use core's spelling.

Hook failures are logged by the host and processing continues. Handle expected failures yourself when silent continuation would leave your plugin inconsistent. If no suitable stable extension point exists, propose one in core rather than coupling to page markup or an internal implementation detail.
