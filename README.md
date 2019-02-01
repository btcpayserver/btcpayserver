
![BTCPay Server](BTCPayServer/wwwroot/img/btc_pay_BG_twitter.png)

[![Docker Automated build](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/btcpayserver/)
[![Deploy to Azure](https://azuredeploy.net/deploybutton.svg)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbtcpayserver%2Fbtcpayserver-azure%2Fmaster%2Fazuredeploy.json)
[![CircleCI](https://circleci.com/gh/btcpayserver/btcpayserver.svg?style=svg)](https://circleci.com/gh/btcpayserver/btcpayserver)

# BTCPay Server

## Introduction 

BTCPay Server is a free and open-source cryptocurrency payment processor which allows you to receive payments in Bitcoin and altcoins directly, with no fees, transaction cost or a middleman.

BTCPay is a non-custodial invoicing system which eliminates the involvement of a third-party. Payments with BTCPay go directly to your wallet, which increases the privacy and security. Your private keys are never uploaded to the server. There is no address re-use since each invoice generates a new address deriving from your xpubkey.

The software is built in C# and conforms to the invoice [API of BitPay](https://bitpay.com/api). It allows for your website to be easily migrated from BitPay and configured as a self-hosted payment processor.

You can run BTCPay as a self-hosted solution on your own server, or use a [third-party host](https://github.com/btcpayserver/btcpayserver-doc/blob/master/ThirdPartyHosting.md).

The self-hosted solution allows you not only to attach an unlimited number of stores and use the Lightning Network but also become the payment processor for others.

Thanks to the [apps](https://github.com/btcpayserver/btcpayserver-doc/blob/master/Apps.md) built on top of it, you can use BTCPay to receive donations, start a crowdfunding campaign or have an in-store Point of Sale.

## Features

* Direct, peer-to-peer Bitcoin and altcoin payments
* No transaction fees (other than those for the crypto networks)
* No processing fees
* No middleman
* No KYC
* User has complete control over private keys
* Enhanced privacy
* Enhanced security
* Self-hosted
* SegWit support
* Lightning Network support (LND and c-lightning)
* Altcoin support
* Full compatibility with BitPay API (easy migration)
* Process payments for others
* Easy-embeddable Payment buttons
* Point of sale app
* Crowdfunding app

## Supported Altcoins

Bitcoin is the only focus of the project and its core developers. However, support is implemented for several altcoins:

* Bitcoin Gold (BTG)
* Bitcoin Plus (XBC)
* Bitcore (BTX)
* Dash (DASH)
* Dogecoin (DOGE)
* Feathercoin (FTC)
* Groestlcoin (GRS)
* Litecoin (LTC)
* Monacoin (MONA)
* Polis (POLIS)
* Viacoin (VIA)

Altcoins are maintained by their respective communities.

## Documentation

Please check out our [complete documentation](https://github.com/btcpayserver/btcpayserver-doc) and [FAQ](https://github.com/btcpayserver/btcpayserver-doc/tree/master/FAQ#btcpay-frequently-asked-questions-and-common-issues) for more details. 

If you have any troubles with BTCPay, please file a [Github issue](https://github.com/btcpayserver/btcpayserver/issues).
For general questions, please join the community chat on [Mattermost](https://chat.btcpayserver.org/).

## How to build

While the documentation advises to use docker-compose, you may want to build BTCPay yourself.

First install .NET Core SDK v2.1.6 as specified by [Microsoft website](https://www.microsoft.com/net/download/dotnet-core/2.1).

On Powershell:
```
.\build.ps1
```

On linux:
```
./build.sh
```

## How to run

Use the `run` scripts to run BTCPayServer, this example shows how to print the available command line arguments of BTCPayServer.

On Powershell:
```
.\run.ps1 --help
```

On linux:
```
./run.sh --help
```

## How to debug

If you want to debug, use Visual Studio Code or Visual studio 2017.

You need to run the development time docker-compose as described [in the test guide](BTCPayServer.Tests/README.md).

You can then run the debugger by using the Launch Profile `Docker-Regtest` on either Visual Studio Code or Visual studio 2017.

If you need to debug ledger wallet interaction, install the development time certificate with:

```bash
# Install development time certificate in the trust store
dotnet dev-certs https --trust
```

Then use the `Docker-Regtest-https` debug profile.



## Other dependencies

For more information, see the documentation: [How to deploy a BTCPay server instance](https://github.com/btcpayserver/btcpayserver-doc/#deployment).
