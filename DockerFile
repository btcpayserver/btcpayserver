FROM microsoft/aspnetcore-build AS builder
WORKDIR /source
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
# Cache some dependencies
RUN cd BTCPayServer && dotnet restore && cd ..
COPY . .
RUN cd BTCPayServer && dotnet publish --output /app/ --configuration Release

FROM microsoft/aspnetcore:2.0.0
WORKDIR /app

RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

COPY --from=builder "/app" .
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]