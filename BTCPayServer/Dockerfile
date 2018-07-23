FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base


ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /app

RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/
RUN dotnet restore BTCPayServer/BTCPayServer.csproj
COPY . .
WORKDIR /src/BTCPayServer
RUN dotnet build BTCPayServer.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish BTCPayServer.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]
