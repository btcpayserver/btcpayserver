# Build and publish a plugin

The projects that perform these jobs are the canonical sources for their procedures:

- Use the [BTCPay Server plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template) to scaffold, register, build, debug, and test a plugin.
- Use [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/) to package and publish releases.
- Browse the [public plugin directory](https://plugin-builder.btcpayserver.org/public/plugins) for released examples.
- Use the Plugin Builder's `/docs` endpoint for its automation API.

Before submitting a build, keep the source repository publicly cloneable, update the plugin version and dependency condition, run the test suite, and document installation and configuration for users. Test the resulting pre-release package on a disposable compatible BTCPay Server instance.

Plugin Builder owns current account verification, repository, packaging, release, listing, review, and metadata policy. Follow the instructions and validation messages shown there rather than relying on a copied checklist in core documentation.

Publishing or listing is not a code review, security audit, or endorsement by BTCPay Server. Plugin authors remain responsible for maintenance, dependency updates, user support, licensing, release notes, and communicating supported BTCPay Server versions.
