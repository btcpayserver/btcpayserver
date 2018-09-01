
![BTCPay Server](BTCPayServer/wwwroot/img/btc_pay_BG_twitter.png)

[![Docker Automated build](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/btcpayserver/)
[![Deploy to Azure](https://azuredeploy.net/deploybutton.svg)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbtcpayserver%2Fbtcpayserver-azure%2Fmaster%2Fazuredeploy.json)

# BTCPay Server

## Introduction 

BTCPay Server is a free and open-source cryptocurrency payment processor which allows you to receive payments in Bitcoin and altcoins directly, with no fees, transaction cost or a middleman.

BTCPay is a non-custodial invoicing system which eliminates the involvement of a third-party. Payments with BTCPay go directly to your wallet, which increases the privacy and security. Your private keys are never uploaded to the server. There is no address re-use, since each invoice generates a new address diriving from your xpub key.

The software is built in C# language and conforms to the invoice API of BitPay. It allows easy migration of your code base to your own, self-hosted payment processor.

You can run BTCPay as a self-hosted solution on your own server, or use a third-party host.

The self-hosted solution allows you not only to attach an unlimited number of stores and use the Lightning Network but also become the payment processor for others.

Thanks to the apps built on top of it, you can use BTCPay to receive donations or have an in-store POS system.

## Features

* Direct, P2P Bitcoin payments
* Lightning Network support (LND and c-lightning)
* Altcoin support
* Complete control over private keys
* Fully compatability with BitPay API (easy migration)
* Enchanced Privacy
* SegWit support
* Process payments for others
* Payment Buttons
* Point of Sale
* No transaction fees
* No processing fees
* No middleman
* No KYC

## Supported Altcoins

In addition to Bitcoin, BTCPay supports the following cryptocurrencies:

* BGold
* Dash
* Dogecoin
* Feathercoin
* Groestlcoin
* Litecoin
* Monacoin
* Polis
* UFO
* Viacoin

## Documentation

Please check out our [complete documentation](https://github.com/btcpayserver/btcpayserver-doc) for more details.

You can also read the [BTCPay Merchant's Guide](https://www.reddit.com/r/Bitcoin/comments/8f1eqf/the_ultimate_guide_to_btcpay_the_free_and/).

## How to build

While the documentation advise using docker-compose, you may want to build yourself outside of development purpose.

First install .NET Core SDK 2.1 as specified by [Microsoft website](https://www.microsoft.com/net/download/dotnet-core).

On Powershell:
```
.\build.ps1
```

On linux:
```
./build.sh
```

## How to run

Use the `run` scripts to run BTCPayServer, this example show how to print the available command line arguments of BTCPayServer.

On Powershell:
```
.\run.ps1 --help
```

On linux:
```
./run.sh --help
```

## Other dependencies

For more information see the documentation [How to deploy a BTCPay server instance](https://github.com/btcpayserver/btcpayserver-doc/#deployment).
