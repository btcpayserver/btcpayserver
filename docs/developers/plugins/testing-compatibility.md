# Plugin testing and compatibility

Plugins bind to in-process .NET contracts, so compatibility requires more than a successful package build.

## Test layers

- Unit-test plugin-owned business logic without starting BTCPay Server.
- Add integration tests for dependency injection, controllers, authorization scope, persistence, migrations, and background services.
- Start the pinned BTCPay Server checkout with the plugin loaded and exercise its main UI and API workflows.
- Test a clean install, restart, disable/enable, uninstall where supported, and upgrade from the last released plugin version.
- Validate both empty and populated databases and failure recovery around external services.
- For UI changes, test supported desktop and mobile layouts and both light and dark themes.

The [plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template) owns the current test project and debug setup. Use its BTCPay Server submodule for reproducible tests instead of an unrelated local checkout.

## Declare compatibility honestly

Pin development to a stable BTCPay Server tag and set the plugin's BTCPay Server dependency condition to the versions actually tested. Before widening it:

1. Update the submodule and target framework through the template's supported process.
2. Build with warnings reviewed.
3. Run automated tests.
4. Exercise installation and upgrade on a disposable instance.
5. Review every core contract and internal service the plugin consumes.

Contracts in `BTCPayServer.Abstractions` are the preferred integration surface, but they can still evolve across major releases. Types and views elsewhere in core are more tightly coupled. Avoid reflection, replacing core service registrations, copied views, and direct assumptions about core database schema.

The Plugin Builder proving that a project packages successfully is not a runtime or security test. Test the produced pre-release package on a non-production instance before releasing it.
