# Configuration

This page explains how the BTCPay Server application reads settings. For the
complete generated command-line option list, see the
[configuration reference](./configuration-reference.md). Run the same BTCPay
Server version you deploy with `--help` when verifying release-specific
behavior.

Docker operators should configure the generated deployment through the
[btcpayserver-docker configuration guide](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docs/configuration.md),
not by adapting commands from this page.

## Configuration Sources

An application setting can be supplied in three forms:

| Source | Example for the `postgres` setting |
|---|---|
| Configuration file | `postgres=Host=localhost;Database=btcpay;...` |
| Environment | `BTCPAY_POSTGRES=Host=localhost;Database=btcpay;...` |
| Command line | `--postgres "Host=localhost;Database=btcpay;..."` |

Configuration-file keys use the canonical option name. Environment variables
use the `BTCPAY_` prefix and an uppercase option name. Command-line options use
`--` followed by the registered name. Not every internal setting is a
registered command-line option; `--help` is authoritative for that interface.
Feature-specific pages document advanced settings that do not have a
command-line form.

Use `--conf <path>` to select a configuration file. Without an explicit path,
BTCPay Server uses the network-specific `settings.config` under its data
directory. Startup logs report the effective network and configuration file.

## Change Settings Safely

1. Check `--help` for the deployed version instead of assuming a setting from
   another release still exists.
2. Make the change in the deployment's persistent configuration source.
3. Keep connection strings, credentials, cookies, and private endpoints out of
   source control and support messages.
4. Restart BTCPay Server; startup settings are not live-reloaded.
5. Inspect startup logs and exercise the affected feature.

Keep environment-specific service wiring in deployment tooling. Avoid placing
Docker Compose procedures or generated-container details in application
documentation.
