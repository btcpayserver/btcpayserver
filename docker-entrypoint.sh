#!/bin/sh

echo "$(grep "$HOSTNAME" /etc/hosts|awk '{print $1}')  host.docker.internal" >> /etc/hosts
exec dotnet BTCPayServer.dll
