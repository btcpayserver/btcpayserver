# Operator Guide

This guide covers the BTCPay Server application from an instance operator's
perspective. Merchant workflows are in the [user guide](../users/README.md).

## Installation

Start with the public
[deployment guide](https://docs.btcpayserver.org/Deployment/) to compare the
available ways to run BTCPay Server, including third-party hosting, cloud and
VPS deployments, dedicated hardware, and manual installation. If you use a
third-party host instead of operating your own instance, the host is responsible
for the server-level work covered by this guide.

For a self-hosted production instance, the official Docker deployment is the
recommended installation method. Use the
[btcpayserver-docker documentation](https://github.com/btcpayserver/btcpayserver-docker/tree/master/docs).
Its installation, configuration, backup, update, and troubleshooting commands
are authoritative for that deployment.

## Configuration

BTCPay Server reads settings from configuration files, environment variables,
and command-line options. For the complete generated command-line option list,
see the [configuration reference](./configuration-reference.md). Run the same
BTCPay Server version you deploy with `--help` when verifying release-specific
behavior.

Docker operators should configure the generated deployment through the
[btcpayserver-docker configuration guide](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docs/configuration.md),
not by adapting commands from this section.

### Configuration Sources

| Source | Example for the `postgres` setting |
|---|---|
| Configuration file | `postgres=Host=localhost;Database=btcpay;...` |
| Environment | `BTCPAY_POSTGRES=Host=localhost;Database=btcpay;...` |
| Command line | `--postgres "Host=localhost;Database=btcpay;..."` |

Configuration-file keys use the canonical option name. Environment variables
use the `BTCPAY_` prefix and an uppercase option name. Command-line options use
`--` followed by the registered name. Not every internal setting is a
registered command-line option; `--help` is authoritative for that interface.
Feature-specific sections document advanced settings that do not have a
command-line form.

Use `--conf <path>` to select a configuration file. Without an explicit path,
BTCPay Server uses the network-specific `settings.config` under its data
directory. Startup logs report the effective network and configuration file.

### Change Settings Safely

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

## Advanced Topics

Server administrators can install community language packs or maintain local
interface translations. See [Backend translations](translations.md).

Deployment authors can implement the optional
[`btcpay-host` integration](./host-integration.md) to expose selected
server-administration actions to BTCPay Server. Most operators do not need to
configure this interface directly.

## Diagnostics

Start with the failing layer and collect evidence before changing the system.

### Triage

1. Record the exact action, timestamp, URL or invoice ID, expected result, and
   actual error.
2. Confirm the deployed BTCPay Server version, network, and deployment method.
3. Reproduce once while collecting application logs.
4. Check whether the failure affects one account or store, all application
   requests, blockchain detection, or the host itself.
5. Review recent changes to versions, configuration, DNS, proxies, certificates,
   database access, node connectivity, plugins, and available disk or memory.

Server administrators can inspect application log files under **Server
Settings > Logs**. Custom deployments should also inspect the process manager,
reverse proxy, PostgreSQL, NBXplorer, Bitcoin node, and Lightning implementation
used by that deployment.

For the official Docker deployment, use its authoritative
[troubleshooting guide](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docs/troubleshooting.md)
and the
[operations guide](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docs/operations.md)
rather than hardcoded container names or commands from another release.

### Common Boundaries

- **Application will not start:** inspect the first startup exception and verify
  the current version's configuration, PostgreSQL connectivity, filesystem
  permissions, and free space.
- **Site is unreachable:** test the application locally, then the reverse proxy,
  DNS, firewall, and TLS termination in that order.
- **Payment is not detected:** verify the transaction was broadcast, then check
  Bitcoin node and NBXplorer synchronization and connectivity. Use the invoice
  ID and transaction ID to correlate logs.
- **Lightning payment fails:** separate BTCPay invoice errors from node
  availability, channel liquidity, route, and implementation-specific errors.
- **Only one store or integration fails:** compare its permissions, wallet,
  payment methods, webhook deliveries, and API-key scope with a working store.

### Request Help Safely

Include the version, deployment method, network, concise reproduction steps,
relevant timestamps, and the smallest useful log excerpt. Redact passwords,
connection strings, API keys, cookies, macaroons, seeds, private keys, customer
data, and private endpoints.

Search existing
[GitHub issues](https://github.com/btcpayserver/btcpayserver/issues) and ask in
the [community support channel](https://chat.btcpayserver.org/btcpayserver/channels/support).
Open an issue only for a reproducible software defect, with deployment-specific
failures directed to the deployment's own issue tracker.
