# Configuration Option Maintenance

Treat each supported configuration source and each consumer as part of a public startup option.

1. Choose one canonical lowercase key consistent with existing settings.
2. Register it in `DefaultConfiguration.CreateCommandLineApplicationCore()` with the correct `CommandOptionType`, an accurate description, and its default.
3. Add a commented example to `DefaultConfiguration.GetDefaultConfigurationFileTemplate()` when it helps operators.
4. Read it through `IConfiguration`, normally with `GetOrDefault<T>(key, defaultValue)`, and make the behavioral default explicit.
5. Use the existing providers rather than reading environment variables directly. The `BTCPAY_` prefix maps `exampleenabled` to `BTCPAY_EXAMPLEENABLED`.
6. Check every consumer. Disabled features must not leave background work running or UI that exposes unavailable behavior.
7. Set the option explicitly in fixtures that depend on non-default behavior; do not weaken the production default for tests.
8. Document the configuration-file key, environment variable, and command-line form. Change deployment manifests only when that deployment should opt in.

Build the affected project, run focused parsing and behavior tests, and verify the option and default in `./run.sh --help`.
