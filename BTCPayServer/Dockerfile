FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY btcpayserver.sln ./
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/
RUN dotnet restore -nowarn:msb3202,nu1503
COPY . .
WORKDIR /src/BTCPayServer
RUN dotnet build -c Release -o /app

FROM build AS publish
RUN dotnet publish -c Release -o /app

FROM base AS final

#modified
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs

ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8

WORKDIR /app

RUN mkdir /datadir
ENV BTCPAY_DATADIR=/datadir
VOLUME /datadir

#end modified


COPY --from=publish /app .
ENTRYPOINT ["dotnet", "BTCPayServer.dll"]
