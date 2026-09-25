# Host Integration

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

## Discover Capabilities

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

## Command Contract

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

## Security

Treat `btcpay-host` as a privileged boundary. Implement only required commands,
validate every argument, invoke programs without a shell, and grant the BTCPay
Server process no broader host access than those commands require.

The official Docker deployment uses restricted SSH and a forced command. Its
transport is implementation-specific, not part of this interface. See the
current
[`btcpay-host` implementation](https://github.com/btcpayserver/btcpayserver-docker/blob/master/btcpay-host).
