# Operator Guide

This guide covers the BTCPay Server application from an instance operator's
perspective. Merchant workflows are in the [user guide](../users/README.md).

For the official Docker deployment, use the
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

## Host Integration

BTCPay Server delegates deployment-specific administration to an executable
named `btcpay-host`. A deployment can implement only the server-administration
features it supports without giving the application general host access.

Host integration is disabled by default. Enable it with
`btcpayhostenabled=true`, `BTCPAY_BTCPAYHOSTENABLED=true`, or
`--btcpayhostenabled`. Providing the executable alone does not enable it.
Override its path with `btcpayhostexecutable`,
`BTCPAY_BTCPAYHOSTEXECUTABLE`, or `--btcpayhostexecutable`.

BTCPay Server invokes it directly:

```text
btcpay-host <command> [arguments]
```

### Discover Capabilities

At startup, BTCPay Server runs `btcpay-host env`. The command must exit with
status `0` and write one JSON object to standard output:

```json
{
  "deploymentType": "example",
  "commands": ["changedomain", "update", "clean", "restart"],
  "routes": {
    "optionalRoutes": ["lnd-rest", "lnd-grpc"],
    "enabledRoutes": ["lnd-rest"]
  }
}
```

- `deploymentType` is a stable deployment identifier included in startup logs.
- `commands` lists the implemented commands and controls which host-backed
  administration actions appear.
- `routes` is optional deployment metadata. `optionalRoutes` lists routes that
  can be switched, and `enabledRoutes` lists the active subset. BTCPay Server
  currently uses this metadata to warn about disabled Docker LND API routes.

Additional JSON properties are ignored. Invalid JSON, a nonzero exit status,
or an unavailable executable disables host-backed features. Discovery runs at
startup. On Linux and macOS, send the BTCPay Server process `SIGHUP` to refresh
capabilities; otherwise restart it after capabilities change.

### Command Contract

| Command | Arguments | Standard output | Enables |
|---|---|---|---|
| `env` | None | Deployment metadata JSON | Capability discovery |
| `showauthorizedkeys` | None | Authorized-keys content as a JSON string | Reading keys in **Server Settings > Services > SSH** |
| `setauthorizedkeys` | Complete authorized-keys content as argument 1 | Ignored | Updating keys in **Server Settings > Services > SSH** |
| `changedomain` | New domain as argument 1 | Ignored | Domain changes in **Server Settings > Maintenance** |
| `update` | None | Ignored | Updates in **Server Settings > Maintenance** |
| `clean` | None | Ignored | Host cleanup in **Server Settings > Maintenance** |
| `restart` | None | Ignored | Deployment restart in **Server Settings > Maintenance** |

The SSH page requires both authorized-key commands. Unknown command names are
ignored, allowing deployment-specific extensions. A command should exit `0`
when accepted; on failure, exit nonzero and write a diagnostic to standard
error. BTCPay Server currently discards output from non-JSON commands, so the
deployment must retain its own operational logs. JSON-producing commands must
reserve standard output for their response.

### Security

Treat `btcpay-host` as a privileged boundary. Implement only required commands,
validate every argument, invoke programs without a shell, and grant the BTCPay
Server process no broader host access than those commands require.

The official Docker deployment uses restricted SSH and a forced command. Its
transport is implementation-specific, not part of this interface. See the
current
[`btcpay-host` implementation](https://github.com/btcpayserver/btcpayserver-docker/blob/master/btcpay-host).

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
