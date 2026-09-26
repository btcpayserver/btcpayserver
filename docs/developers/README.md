# Developer documentation

Choose the track that matches what you are building.

## Integrate through the API

Use the Greenfield REST API to connect a commerce application, automate a BTCPay Server instance, or build a client library.

- [Get started and plan an integration](api/README.md)
- [Authenticate and request permissions](api/authentication.md)
- [Use cURL, Node.js, or PHP](api/examples.md)
- [Implement or change API endpoints](api/compatibility.md)

The interactive API reference is available at `/docs` on every BTCPay Server instance and at [docs.btcpayserver.org](https://docs.btcpayserver.org/API/Greenfield/v1/).

## Build a plugin

Plugins are in-process .NET extensions. They can add services, controllers, UI, hooks, permissions, data, and API endpoints.

- [Get started](plugins/README.md)
- [Architecture and lifecycle](plugins/architecture-lifecycle.md)
- [UI extension points and hooks](plugins/ui-hooks.md)
- [Authentication and permissions](plugins/permissions.md)
- [Data and migrations](plugins/data-migrations.md)
- [API and Swagger](plugins/api-swagger.md)
- [Testing and compatibility](plugins/testing-compatibility.md)
- [Build and publish](plugins/publishing.md)

BTCPay Server owns the plugin framework contracts documented here. The [plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template) owns scaffolding and development setup, while [Plugin Builder](https://plugin-builder.btcpayserver.org/) owns packaging, release, and listing policy.
