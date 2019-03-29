FROM mcr.microsoft.com/dotnet/core/sdk:2.1.505-alpine3.7 AS builder
WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer.csproj
RUN dotnet restore
COPY BTCPayServer/. .
RUN dotnet publish --output /app/ --configuration Release

FROM mcr.microsoft.com/dotnet/core/aspnet:2.1.9-alpine3.7

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /app
RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
COPY docker-entrypoint.sh docker-entrypoint.sh
ENTRYPOINT ["/app/docker-entrypoint.sh"]
