FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /app

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY BTCPayServer.Tests/BTCPayServer.Tests.csproj BTCPayServer.Tests/
RUN dotnet restore BTCPayServer.Tests/BTCPayServer.Tests.csproj
COPY . .
WORKDIR /src/BTCPayServer.Tests/BTCPayServer.Tests
RUN dotnet build BTCPayServer.Tests.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish  BTCPayServer.Tests.csproj  -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "test"]

