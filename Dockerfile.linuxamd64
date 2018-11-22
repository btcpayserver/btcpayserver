FROM microsoft/dotnet:2.1.500-sdk-alpine3.7 AS builder
WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer.csproj
# Cache some dependencies
RUN dotnet restore
COPY BTCPayServer/. .
RUN dotnet publish --output /app/ --configuration Release

FROM microsoft/dotnet:2.1.6-aspnetcore-runtime-alpine3.7

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /app
# This should be removed soon https://github.com/dotnet/corefx/issues/30003
RUN apk add --no-cache curl 
RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]
