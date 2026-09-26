---
name: btcpayserver-configuration
description: Use when adding or reviewing BTCPay Server startup configuration options. Covers command-line registration, defaults, environment variables, tests, and documentation.
---

# BTCPay Server Configuration Options

Follow [Configuration option maintenance](../../../docs/maintainers/README.md#configuration-option-maintenance).

## Verification

- Build the affected project.
- Run focused tests for configuration parsing and every behavior gated by the option.
- Run the application with `--help` and confirm the new command-line option is listed with an accurate description and default.
