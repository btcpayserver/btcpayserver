#!/bin/sh

echo "$(/sbin/ip route|awk '/default/ { print $3 }')  host.docker.internal" >> /etc/hosts

if ! [ -f "$BTCPAY_SSHKEYFILE" ]; then
    echo "Creating BTCPay Server SSH key File..."
    ssh-keygen -t rsa -f "$BTCPAY_SSHKEYFILE" -q -P "" -m PEM -C btcpayserver > /dev/null
    [ -f "$BTCPAY_SSHAUTHORIZEDKEYS" ] && sed -i '/btcpayserver$/d' "$BTCPAY_SSHAUTHORIZEDKEYS"
fi

if [ -f "$BTCPAY_SSHAUTHORIZEDKEYS" ] && ! grep -q "btcpayserver$" "$BTCPAY_SSHAUTHORIZEDKEYS"; then
    echo "Adding BTCPay Server SSH key to authorized keys"
    cat "$BTCPAY_SSHKEYFILE.pub" >> "$BTCPAY_SSHAUTHORIZEDKEYS"
fi

exec dotnet BTCPayServer.dll
