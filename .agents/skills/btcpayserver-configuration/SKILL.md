---
name: btcpayserver-configuration
description: Use when adding or reviewing BTCPay Server startup configuration options. Covers command-line registration, defaults, environment variables, tests, and documentation.
---

# BTCPay Server Configuration Options

When adding a public startup configuration option, treat every supported configuration source as part of the feature.

## Implementation Checklist

- Choose one canonical lowercase key consistent with existing settings.
- Register the option in `DefaultConfiguration.CreateCommandLineApplicationCore()` so the command line accepts it and `--help` documents it. Use the appropriate `CommandOptionType`, such as `BoolValue` or `SingleValue`, and include the default in the help text.
- Add the setting and its default as a commented example in `DefaultConfiguration.GetDefaultConfigurationFileTemplate()` when it is useful to operators.
- Read the setting through `IConfiguration`, normally with `GetOrDefault<T>(key, defaultValue)`, and make the default explicit at the point where behavior is selected.
- Use the existing configuration providers rather than reading an environment variable directly. `DefaultConfiguration.EnvironmentVariablePrefix` maps a setting such as `exampleenabled` to `BTCPAY_EXAMPLEENABLED`.
- Check every consumer of the affected feature. A disabled-by-default option must prevent background work and hide or disable UI that would otherwise expose an unavailable feature.
- Update test configuration explicitly when a fixture depends on behavior that is no longer the default. Do not weaken the production default to preserve a test assumption.
- Document all operator-facing forms that are supported: configuration-file key, `BTCPAY_` environment variable, and command-line option. Update deployment manifests separately when a deployment intentionally opts in.

## Verification

- Build the affected project.
- Run focused tests for configuration parsing and every behavior gated by the option.
- Run the application with `--help` and confirm the new command-line option is listed with an accurate description and default.
