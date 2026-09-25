# Local Development

## Prerequisites

- Install the .NET SDK required by `Build/Common.csproj` (currently .NET 10).
- Install Docker with Compose for the local PostgreSQL, NBXplorer, Bitcoin, Lightning, Tor, and Mailpit services.
- Use Visual Studio 2022 or JetBrains Rider for the repository launch profiles and debugging.

The broader platform setup guide is in the [public local development documentation](https://docs.btcpayserver.org/Development/LocalDevelopment/).

## Build

Build the solution directly:

```sh
dotnet build btcpayserver.sln
```

Create the release publish output used by the run scripts:

```sh
./build.sh
```

On PowerShell, use `./build.ps1`.

## Run

Start the development dependencies from `BTCPayServer.Tests`:

```sh
docker-compose up -d dev
```

After running the build script, start the published application or inspect its options:

```sh
./run.sh
./run.sh --help
```

On PowerShell, use `./run.ps1`. For debugger-driven development, use the `Docker-Regtest` launch profile. The `Docker-Regtest-https` profile also requires a trusted development certificate:

```sh
dotnet dev-certs https --trust
```

See [testing](testing.md) for focused test commands and regtest tooling.
