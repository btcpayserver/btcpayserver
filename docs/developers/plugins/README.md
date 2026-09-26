# Plugin development

BTCPay Server plugins are .NET assemblies loaded into the server process. They have the same trust and failure boundary as core code: a plugin can access registered services and data, and a faulty plugin can prevent startup or compromise the instance.

## Start from the template

Use the [BTCPay Server plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template) for the current project layout, target framework, registration script, test project, and BTCPay Server submodule workflow. Follow its README to:

1. Clone with submodules.
2. Pin the BTCPay Server submodule to the stable version you support.
3. Rename the template assembly and update its package metadata.
4. Set the BTCPay Server dependency condition.
5. Register, build, and debug the plugin against the included server checkout.

Do not reproduce that scaffolding by hand from this documentation; the template changes with the supported toolchain.

## Learn the framework

- [Architecture and lifecycle](architecture-lifecycle.md)
- [UI extension points and hooks](ui-hooks.md)
- [Authentication and permissions](permissions.md)
- [Data and migrations](data-migrations.md)
- [API and Swagger](api-swagger.md)
- [Testing and compatibility](testing-compatibility.md)
- [Build and publish](publishing.md)

Core framework contracts live in [`BTCPayServer.Abstractions`](https://github.com/btcpayserver/btcpayserver/tree/master/BTCPayServer.Abstractions). Core's built-in plugins under [`BTCPayServer/Plugins`](https://github.com/btcpayserver/btcpayserver/tree/master/BTCPayServer/Plugins) are useful examples, but internal services outside the abstractions project can change between releases. Prefer an explicit extension contract when one exists.
