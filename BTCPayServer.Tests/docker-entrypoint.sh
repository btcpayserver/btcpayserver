#!/bin/sh
set -e

dotnet test --filter Fast=Fast --no-build
dotnet test --filter Integration=Integration --no-build -v n
