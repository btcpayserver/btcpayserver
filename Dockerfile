FROM microsoft/dotnet:2.1.300-rc1-sdk-alpine3.7 AS builder
WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer.csproj
# Cache some dependencies
RUN dotnet restore
COPY BTCPayServer/. .
RUN dotnet publish --output /app/ --configuration Release

FROM microsoft/dotnet:2.1.0-rc1-aspnetcore-runtime-alpine3.7
WORKDIR /app

RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]
