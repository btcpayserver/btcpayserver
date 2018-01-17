FROM microsoft/aspnetcore-build:2.0.5-2.1.4-stretch AS builder
WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer.csproj
# Cache some dependencies
RUN dotnet restore
COPY BTCPayServer/. .
RUN dotnet publish --output /app/ --configuration Release

FROM microsoft/aspnetcore:2.0.5-stretch
WORKDIR /app

RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]
