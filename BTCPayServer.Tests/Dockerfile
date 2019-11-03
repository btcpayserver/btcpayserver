FROM mcr.microsoft.com/dotnet/core/sdk:2.1.505-alpine3.7 AS builder
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /source
COPY nuget.config nuget.config
COPY Build/Common.csproj Build/Common.csproj
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY BTCPayServer.Common/BTCPayServer.Common.csproj BTCPayServer.Common/BTCPayServer.Common.csproj
COPY BTCPayServer.Rating/BTCPayServer.Rating.csproj BTCPayServer.Rating/BTCPayServer.Rating.csproj
COPY BTCPayServer.Data/BTCPayServer.Data.csproj BTCPayServer.Data/BTCPayServer.Data.csproj
COPY BTCPayServer.Tests/BTCPayServer.Tests.csproj BTCPayServer.Tests/BTCPayServer.Tests.csproj
RUN dotnet restore BTCPayServer.Tests/BTCPayServer.Tests.csproj

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

RUN apk add --no-cache chromium chromium-chromedriver icu-libs

ENV SCREEN_HEIGHT 600 \
    SCREEN_WIDTH 1200

COPY . .
RUN cd BTCPayServer.Tests && dotnet build /p:CI_TESTS=true
WORKDIR /source/BTCPayServer.Tests
ENTRYPOINT ["./docker-entrypoint.sh"]
