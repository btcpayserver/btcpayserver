dotnet restore
dotnet publish -c Release
docker-compose -f docker-compose.regtest.yml up --force-recreate --build