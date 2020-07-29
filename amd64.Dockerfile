FROM mcr.microsoft.com/dotnet/core/sdk:3.1.202 AS builder
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /source
COPY nuget.config nuget.config
COPY Build/Common.csproj Build/Common.csproj
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY BTCPayServer.Common/BTCPayServer.Common.csproj BTCPayServer.Common/BTCPayServer.Common.csproj
COPY BTCPayServer.Rating/BTCPayServer.Rating.csproj BTCPayServer.Rating/BTCPayServer.Rating.csproj
COPY BTCPayServer.Data/BTCPayServer.Data.csproj BTCPayServer.Data/BTCPayServer.Data.csproj
COPY BTCPayServer.Client/BTCPayServer.Client.csproj BTCPayServer.Client/BTCPayServer.Client.csproj
RUN cd BTCPayServer && dotnet restore
COPY BTCPayServer.Common/. BTCPayServer.Common/.
COPY BTCPayServer.Rating/. BTCPayServer.Rating/.
COPY BTCPayServer.Data/. BTCPayServer.Data/.
COPY BTCPayServer.Client/. BTCPayServer.Client/.
COPY BTCPayServer/. BTCPayServer/.
COPY Build/Version.csproj Build/Version.csproj
ARG CONFIGURATION_NAME=Release
RUN cd BTCPayServer && dotnet publish --output /app/ --configuration ${CONFIGURATION_NAME}

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1.4-buster-slim

RUN apt-get update && apt-get install -y --no-install-recommends iproute2 openssh-client \
    && rm -rf /var/lib/apt/lists/* 

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /datadir
WORKDIR /app
ENV BTCPAY_DATADIR=/datadir
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
VOLUME /datadir

COPY --from=builder "/app" .
COPY docker-entrypoint.sh docker-entrypoint.sh
ENTRYPOINT ["/app/docker-entrypoint.sh"]
