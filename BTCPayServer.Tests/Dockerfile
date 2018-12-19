FROM microsoft/dotnet:2.1.500-sdk-alpine3.7 AS builder
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

# This should be removed soon https://github.com/dotnet/corefx/issues/30003
RUN apk add --no-cache curl 

WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY BTCPayServer.Tests/BTCPayServer.Tests.csproj BTCPayServer.Tests/BTCPayServer.Tests.csproj
RUN dotnet restore BTCPayServer.Tests/BTCPayServer.Tests.csproj
COPY . .
RUN dotnet build
WORKDIR /source/BTCPayServer.Tests
ENTRYPOINT ["./docker-entrypoint.sh"]
