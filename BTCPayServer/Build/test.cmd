pushd .
cd ..
dotnet publish -c Release
popd
robocopy ..\bin\Release\netcoreapp2.0\publish\ dockerfiles /e
docker-compose up --force-recreate --build