# How to be started for development

BTCPay Server tests depend on having a proper environment running with Postgres, Bitcoind, NBxplorer configured.
You can however use the `docker-compose.yml` of this folder to get it running.

In addition, when you run a debug session of BTCPay (Hitting F5 on Visual Studio Code or Visual Studio 2017), it will run the launch profile called `Docker-Regtest`. This launch profile depends on this `docker-compose` running.

This is running a bitcoind instance on regtest, a private bitcoin blockchain for testing on which you can generate blocks yourself.

```
docker-compose up dev
```

You can run the tests while it is running through your favorite IDE, or with

```
dotnet test
```

Once you want to stop

```
docker-compose down
```

If you want to stop, and remove all existing data

```
docker-compose down -v
```

You can run the tests inside a container by running

```
docker-compose run --rm tests
```

## Send commands to bitcoind

You can call bitcoin-cli inside the container with `docker exec`, for example, if you want to send `0.23111090` to `mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf`:
```
docker exec -ti btcpayserver_dev_bitcoind bitcoin-cli -regtest -conf="/data/bitcoin.conf" -datadir="/data" sendtoaddress "mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf" 0.23111090
```

If you are using Powershell:
```
.\docker-bitcoin-cli.ps1 sendtoaddress "mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf" 0.23111090
```
