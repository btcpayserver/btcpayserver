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

You can run the tests inside a container by running

```
docker-compose run --rm tests
```