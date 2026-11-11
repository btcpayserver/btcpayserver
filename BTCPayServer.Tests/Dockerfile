FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble AS builder

WORKDIR /source
COPY nuget.config nuget.config
COPY Build/Common.csproj Build/Common.csproj
COPY BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY BTCPayServer.Common/BTCPayServer.Common.csproj BTCPayServer.Common/BTCPayServer.Common.csproj
COPY BTCPayServer.Rating/BTCPayServer.Rating.csproj BTCPayServer.Rating/BTCPayServer.Rating.csproj
COPY BTCPayServer.Data/BTCPayServer.Data.csproj BTCPayServer.Data/BTCPayServer.Data.csproj
RUN cd BTCPayServer && dotnet restore
COPY BTCPayServer.Common/. BTCPayServer.Common/.
COPY BTCPayServer.Rating/. BTCPayServer.Rating/.
COPY BTCPayServer.Data/. BTCPayServer.Data/.
COPY BTCPayServer/. BTCPayServer/.
COPY Build/Version.csproj Build/Version.csproj

ENV SCREEN_HEIGHT 600 \
    SCREEN_WIDTH 1200

COPY . .

ARG CONFIGURATION_NAME=Release
RUN cd BTCPayServer.Tests && dotnet build --configuration ${CONFIGURATION_NAME} /p:CI_TESTS=true /p:RazorCompileOnBuild=true
RUN dotnet tool install --global Microsoft.Playwright.CLI
ENV PATH="$PATH:/root/.dotnet/tools"
# Keep browser dependencies available during an Ubuntu archive DNS outage.
RUN printf '%s\n' 'http://archive.ubuntu.com/ubuntu/' 'https://mirrors.edge.kernel.org/ubuntu/' > /etc/apt/ubuntu-mirrors.txt \
    && sed -i -E 's|http://(archive|security)\.ubuntu\.com/ubuntu/?|mirror+file:/etc/apt/ubuntu-mirrors.txt|g' /etc/apt/sources.list.d/ubuntu.sources \
    && playwright install chromium --with-deps
WORKDIR /source/BTCPayServer.Tests
ENV CONFIGURATION_NAME=${CONFIGURATION_NAME}
ENTRYPOINT ["./docker-entrypoint.sh"]
