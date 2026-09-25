# Diagnostics

Start with the failing layer and collect evidence before changing the system.

## Triage

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

## Common Boundaries

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

## Request Help Safely

Include the version, deployment method, network, concise reproduction steps,
relevant timestamps, and the smallest useful log excerpt. Redact passwords,
connection strings, API keys, cookies, macaroons, seeds, private keys, customer
data, and private endpoints.

Search existing
[GitHub issues](https://github.com/btcpayserver/btcpayserver/issues) and ask in
the [community support channel](https://chat.btcpayserver.org/btcpayserver/channels/support).
Open an issue only for a reproducible software defect, with deployment-specific
failures directed to the deployment's own issue tracker.
