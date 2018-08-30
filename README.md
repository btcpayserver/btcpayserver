
![BTCPay Server](BTCPayServer/wwwroot/img/btc_pay_BG_twitter.png)

[![Docker Automated build](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/nicolasdorier/btcpayserver/)
[![Deploy to Azure](https://azuredeploy.net/deploybutton.svg)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbtcpayserver%2Fbtcpayserver-azure%2Fmaster%2Fazuredeploy.json)

# BTCPay Server

## Introduction 

BTCPay Server is an Open Source payment processor, written in C#, that conforms to the invoice API of [Bitpay](https://bitpay.com/).
This allows easy migration of your code base to your own, self-hosted payment processor.

This solution is for you if:

* You are currently using Bitpay as a payment processor but are worried about their commitment to Bitcoin in the future
* You want to be in control of your own funds
* Bitpay compliance team decided to reject your application
* You want lower fees (we support Segwit)
* You want to become a payment processor yourself and offer a BTCPay hosted solution to merchants
* You want a way to support currencies other than those offered by Bitpay

## We support altcoins!

In addition to Bitcoin, we support the following crypto currencies:

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

You can also checkout [The Merchants Guide to accepting Bitcoin directly with no intermediates through BTCPay](https://www.reddit.com/r/Bitcoin/comments/81h1oy/the_merchants_guide_to_accepting_bitcoin_directly/).

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
