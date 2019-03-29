FROM mcr.microsoft.com/dotnet/core/sdk:2.1.505-alpine3.7 AS builder
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY BTCPayServer.Tests/BTCPayServer.Tests.csproj BTCPayServer.Tests/BTCPayServer.Tests.csproj
RUN dotnet restore BTCPayServer.Tests/BTCPayServer.Tests.csproj
COPY . .
RUN dotnet build
WORKDIR /source/BTCPayServer.Tests
ENTRYPOINT ["./docker-entrypoint.sh"]
