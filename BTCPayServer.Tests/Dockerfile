FROM mcr.microsoft.com/dotnet/core/sdk:2.1.505-alpine3.7 AS builder
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY BTCPayServer.Tests/BTCPayServer.Tests.csproj BTCPayServer.Tests/BTCPayServer.Tests.csproj
RUN dotnet restore BTCPayServer.Tests/BTCPayServer.Tests.csproj

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

RUN apk add --no-cache chromium chromium-chromedriver icu-libs

ENV SCREEN_HEIGHT 600 \
    SCREEN_WIDTH 1200

COPY . .
RUN dotnet build
WORKDIR /source/BTCPayServer.Tests
ENTRYPOINT ["./docker-entrypoint.sh"]
