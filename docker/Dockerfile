#syntax=docker/dockerfile:1.2
FROM mcr.microsoft.com/dotnet/sdk:3.1.413-bullseye AS builder

COPY . /btcp

RUN cd /btcp/BTCPayServer && dotnet publish --output /app/ -c Altcoins-Release

FROM mcr.microsoft.com/dotnet/aspnet:3.1.19-bullseye-slim

WORKDIR /root
ENV BTCPAY_DATADIR=/datadir
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
VOLUME /datadir

COPY --from=builder /app ./
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]
