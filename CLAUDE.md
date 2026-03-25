# BTCPay Server

## Project Overview

Free, open-source, self-hosted Bitcoin payment processor. Accept Bitcoin payments without fees or intermediaries. Supports on-chain Bitcoin and Lightning Network (LND, CLN, Eclair). Non-custodial, Tor-compatible, multi-tenant.

## Architecture

**Solution**: `btcpayserver.sln`

### Core Projects

- **`BTCPayServer/`** — Main web application (ASP.NET Core). UI, controllers, views, services.
- **`BTCPayServer.Abstractions/`** — Shared interfaces and abstractions used across projects.
- **`BTCPayServer.Common/`** — Shared utility code and common domain logic.
- **`BTCPayServer.Data/`** — Data access layer (EF Core, migrations, repositories).
- **`BTCPayServer.Client/`** — Client library for the Greenfield API.
- **`BTCPayServer.Rating/`** — Currency rating/exchange rate provider.
- **`BTCPayServer.PluginPacker/`** — Plugin packaging utilities.
- **`BTCPayServer.Tests/`** — Test suite.

### Build & Infrastructure

- **`Build/`** — Build scripts and shared build configuration (`Common.csproj`, `Version.csproj`).
- **`docs/`** — Documentation source (published to docs.btcpayserver.org).

### Key Files

- `build.sh` / `build.ps1` — Build scripts: `dotnet publish -c Release`
- `run.sh` / `run.ps1` — Run the compiled server
- `Dockerfile` — Container build
- `docker-entrypoint.sh` — Container entrypoint
- `nuget.config` — NuGet feed configuration

## Build & Development

```bash
# Build
./build.sh                    # or build.ps1 on Windows
dotnet publish --no-cache -o BTCPayServer/bin/Release/publish/ -c Release BTCPayServer/BTCPayServer.csproj

# Run
./run.sh                      # or run.ps1 on Windows
cd BTCPayServer/bin/Release/publish/ && dotnet BTCPayServer.dll

# Test
dotnet test BTCPayServer.Tests/BTCPayServer.Tests.csproj

# Docker
docker build -t btcpayserver .
```

## Technology Stack

- **Language**: C# (.NET 8+)
- **Web framework**: ASP.NET Core
- **Database**: PostgreSQL (production), SQLite (development/lightweight)
- **ORM**: Entity Framework Core
- **Frontend**: Razor views, vanilla JS
- **CI**: CircleCI (`.circleci/`)
- **Code review**: CodeRabbit (`.coderabbit.yaml`)
- **Container**: Docker

## Key Integrations

- **Bitcoin implementations**: Bitcoin Core (full node), btcd
- **Lightning Network**: LND, Core Lightning (CLN), Eclair
- **Altcoins**: Separate community-maintained build
- **Payment methods**: On-chain, Lightning, LNURL
- **Hardware wallets**: Supported via vault integration
- **Tor**: Native support for .onion services

## API

- **Greenfield API** (v1): REST API at `/api/v1/` — see docs at https://docs.btcpayserver.org/API/Greenfield/v1/
- Client library available in `BTCPayServer.Client/`

## Contributing

- PRs welcome (see badge in README)
- Contribution guide: https://docs.btcpayserver.org/Contribute/
- Community chat: https://chat.btcpayserver.org/
- Report bugs via GitHub Issues
- Feature requests via GitHub Discussions

## Conventions

- Follow `.editorconfig` for code style
- MIT licensed
- Default branch: `master`
- Release process documented in `RELEASE-CHECKLIST.md` and `RELEASE-CYCLES.md`
- Security policy in `SECURITY.md`
