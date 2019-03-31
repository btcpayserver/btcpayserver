#!/bin/sh

echo "$(/sbin/ip route|awk '/default/ { print $3 }')  host.docker.internal" >> /etc/hosts
exec dotnet BTCPayServer.dll
