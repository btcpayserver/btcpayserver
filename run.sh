#!/bin/bash

dotnet run --no-launch-profile --no-build -c Release -p "BTCPayServer/BTCPayServer.csproj" -- $@
