# Plugin architecture and lifecycle

A plugin implements `IBTCPayServerPlugin`, normally by deriving from `BaseBTCPayServerPlugin`. Its identifier, name, version, and description default to assembly metadata. Keep the identifier stable after release because installation state and dependencies refer to it.

## Registration phase

`Execute(IServiceCollection services)` runs while BTCPay Server is building its service collection. Register controllers, services, hosted services, UI extensions, policies, migrations, hooks, and Swagger providers here.

```csharp
public sealed class Plugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "Example.Plugin";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<ExampleService>();
        services.AddUIExtension("header-nav", "/Views/Shared/ExampleNav.cshtml");
    }
}
```

Choose service lifetimes using normal ASP.NET Core rules. A singleton must not capture a scoped service. Hosted services should honor cancellation and must not make startup depend indefinitely on an external system.

## Application phase

`Execute(IApplicationBuilder, IServiceProvider)` runs after the application's service provider exists. Use it only when application-pipeline setup cannot be expressed through service registration. Most plugins need only the service-collection overload.

## Metadata and dependencies

`Dependencies` declares required plugin identifiers and version conditions. The template includes the correct dependency on BTCPay Server for its pinned release. Update that condition intentionally when testing against a newer server; do not claim compatibility from compilation alone.

The host loads plugin code in process. There is no security sandbox or per-plugin resource isolation. Avoid static mutable state, blocking startup, unbounded background work, and broad access to secrets. Treat every dependency injection and route registration as part of the server's process-wide composition.

See the current [`IBTCPayServerPlugin`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Abstractions/Contracts/IBTCPayServerPlugin.cs) and [`BaseBTCPayServerPlugin`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Abstractions/Models/BaseBTCPayServerPlugin.cs) contracts.
