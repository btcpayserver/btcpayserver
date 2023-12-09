# This is a manifest image, will pull the image with the same arch as the builder machine
FROM mcr.microsoft.com/dotnet/sdk:8.0.100-bookworm-slim AS builder
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV LC_ALL en_US.UTF-8
RUN apt-get update \
    && apt-get install -qq --no-install-recommends qemu-system-arm qemu-user-static qemu-user binfmt-support

WORKDIR /source
COPY nuget.config nuget.config
COPY Build/Common.csproj Build/Common.csproj
COPY BTCPayServer.Abstractions/BTCPayServer.Abstractions.csproj BTCPayServer.Abstractions/BTCPayServer.Abstractions.csproj
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
COPY BTCPayServer.Abstractions/. BTCPayServer.Abstractions/.
COPY BTCPayServer/. BTCPayServer/.
COPY Build/Version.csproj Build/Version.csproj
ARG CONFIGURATION_NAME=Release
ARG GIT_COMMIT
RUN cd BTCPayServer && dotnet publish -p:GitCommit=${GIT_COMMIT} --output /app/ --configuration ${CONFIGURATION_NAME}

# Force the builder machine to take make an arm runtime image. This is fine as long as the builder does not run any program
FROM mcr.microsoft.com/dotnet/aspnet:8.0.0-bookworm-slim-arm64v8
COPY --from=builder /usr/bin/qemu-aarch64-static /usr/bin/qemu-aarch64-static
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
