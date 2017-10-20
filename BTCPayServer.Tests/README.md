# How to run the tests

The tests depends on having a proper environment running with Postgres, Bitcoind, NBxplorer configured.
You can however use the `docker-compose.yml` of this folder to get it running.

```
docker-compose up nbxplorer
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

The Bitcoin RPC server is exposed to the host, for example, you can send 0.23111090 BTC to mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf.

```
bitcoin-cli -regtest -rpcport=43782 -rpcuser=ceiwHEbqWI83 -rpcpassword=DwubwWsoo3 sendtoaddress "mohu16LH66ptoWGEL1GtP6KHTBJYXMWhEf" 0.23111090
```