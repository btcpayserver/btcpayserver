#!/bin/sh

echo "$(/sbin/ip route|awk '/default/ { print $3 }')  host.docker.internal" >> /etc/hosts

if [ ! -z "$BTCPAY_SSHKEYFILE" ] && ! [ -f "$BTCPAY_SSHKEYFILE" ]; then
    echo "Creating BTCPay Server SSH key File..."
    ssh-keygen -t rsa -f "$BTCPAY_SSHKEYFILE" -q -P "" -m PEM -C btcpayserver > /dev/null
    if [ -f "$BTCPAY_SSHAUTHORIZEDKEYS" ]; then
        # Because the file is mounted, set -i does not work
        sed '/btcpayserver$/d' "$BTCPAY_SSHAUTHORIZEDKEYS" > "$BTCPAY_SSHAUTHORIZEDKEYS.new"
        cat "$BTCPAY_SSHAUTHORIZEDKEYS.new" > "$BTCPAY_SSHAUTHORIZEDKEYS"
        rm -rf "$BTCPAY_SSHAUTHORIZEDKEYS.new"
    fi
fi

if [ ! -z "$BTCPAY_SSHKEYFILE" ] && [ -f "$BTCPAY_SSHAUTHORIZEDKEYS" ] && ! grep -q "btcpayserver$" "$BTCPAY_SSHAUTHORIZEDKEYS"; then
    echo "Adding BTCPay Server SSH key to authorized keys"
    cat "$BTCPAY_SSHKEYFILE.pub" >> "$BTCPAY_SSHAUTHORIZEDKEYS"
fi

exec dotnet BTCPayServer.dll
