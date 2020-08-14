# Tooling

This README describe some useful tooling that you may need during development and testing.
To learn how to get started with your local development environment, read [our documentation](https://docs.btcpayserver.org/LocalDevelopment/).

## How to manually test payments

### Using the test bitcoin-cli

You can call bitcoin-cli inside the container with `docker exec`.
For example, if you want to send `0.23111090` to `mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf`:

```sh
./docker-bitcoin-cli.sh sendtoaddress "mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf" 0.23111090
```

If you are using Powershell:

```powershell
.\docker-bitcoin-cli.ps1 sendtoaddress "mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf" 0.23111090
```

You can also generate blocks:

```powershell
.\docker-bitcoin-generate.ps1 3
```

### Using the test litecoin-cli

Same as bitcoin-cli, but with `.\docker-litecoin-cli.ps1` and `.\docker-litecoin-cli.sh` instead.

### Using the test lightning-cli

If you are using Linux:

```sh
./docker-customer-lightning-cli.sh pay lnbcrt100u1pd2e6uspp5ajnadvhazjrz55twd5k6yeg9u87wpw0q2fdr7g960yl5asv5fmnqdq9d3hkccqpxmedyrk0ehw5ueqx5e0r4qrrv74cewddfcvsxaawqz7634cmjj39sqwy5tvhz0hasktkk6t9pqfdh3edmf3z09zst5y7khv3rvxh8ctqqw6mwhh
```

If you are using Powershell:

```powershell
.\docker-customer-lightning-cli.ps1 pay lnbcrt100u1pd2e6uspp5ajnadvhazjrz55twd5k6yeg9u87wpw0q2fdr7g960yl5asv5fmnqdq9d3hkccqpxmedyrk0ehw5ueqx5e0r4qrrv74cewddfcvsxaawqz7634cmjj39sqwy5tvhz0hasktkk6t9pqfdh3edmf3z09zst5y7khv3rvxh8ctqqw6mwhh
```

If you get this message:

```json
{ "code" : 205, "message" : "Could not find a route", "data" : { "getroute_tries" : 1, "sendpay_tries" : 0 } }
```

Please, run the test `CanSetLightningServer`, this will establish a channel between the customer and the merchant, then, retry.

Alternatively you can run the `./docker-lightning-channel-setup.sh` script to establish the channel connection.
The `./docker-lightning-channel-teardown.sh` script closes any existing lightning channels.

## FAQ

`docker-compose up dev` failed or tests are not passing, what should I do?

1. Run `docker-compose down --v` (this will reset your test environment)
2. Run `docker-compose pull` (this will ensure you have the lastest images)
3. Run again with `docker-compose up dev`

If you still have issues, try to restart docker.
