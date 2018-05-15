FROM microsoft/dotnet:2.1-runtime AS base
WORKDIR /app

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY btcpayserver.sln ./
COPY BTCPayServer.Tests/BTCPayServer.Tests.csproj BTCPayServer.Tests/
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/
RUN dotnet restore -nowarn:msb3202,nu1503
COPY . .
WORKDIR /src/BTCPayServer.Tests
RUN dotnet build -c Release -o /app

FROM build AS publish
RUN dotnet publish -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BTCPayServer.Tests.dll"]
