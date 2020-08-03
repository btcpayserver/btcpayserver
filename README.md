
![BTCPay Server](BTCPayServer/wwwroot/img/btc_pay_BG_twitter.png)

[![Docker Automated build](https://img.shields.io/docker/automated/jrottenberg/ffmpeg.svg)](https://hub.docker.com/r/btcpayserver/btcpayserver/)
[![Deploy to Azure](https://azuredeploy.net/deploybutton.svg)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbtcpayserver%2Fbtcpayserver-azure%2Fmaster%2Fazuredeploy.json)
[![CircleCI](https://circleci.com/gh/btcpayserver/btcpayserver.svg?style=svg)](https://circleci.com/gh/btcpayserver/btcpayserver)

# BTCPay Server

## Introduction

BTCPay Server is a free and open-source cryptocurrency payment processor which allows you to receive payments in Bitcoin and altcoins directly, with no fees, transaction cost or a middleman.

BTCPay Server is a non-custodial invoicing system which eliminates the involvement of a third-party. Payments with BTCPay Server go directly to your wallet, which increases the privacy and security. Your private keys are never uploaded to the server. There is no address re-use, since each invoice generates a new address deriving from your xpubkey.

The software is built in C# and conforms to the invoice [API of BitPay](https://bitpay.com/api). It allows for your website to be easily migrated from BitPay and configured as a self-hosted payment processor.

You can run BTCPay Server as a self-hosted solution on your own server, or use a [third-party host](https://docs.btcpayserver.org/ThirdPartyHosting/).

The self-hosted solution allows you not only to attach an unlimited number of stores and use the Lightning Network but also become the payment processor for others.

Thanks to the [apps](https://docs.btcpayserver.org/Apps/) built on top of it, you can use BTCPay to receive donations, start a crowdfunding campaign or have an in-store Point of Sale.

[![What is BTCPay](https://img.youtube.com/vi/q7xJMno_B3U/sddefault.jpg)](https://www.youtube.com/watch?v=q7xJMno_B3U "What is BTCPay")

## Table of Contents

* [Features](#features)
* [Getting Started](#getting-started)
* [Documentation](#documentation)
* [Contributing](#Contributing)
* [How to build](#how-to-build)
* [How to run](#how-to-run)
* [How to debug](#how-to-debug)
* [Dependencies](#other-dependencies)

## Features

* Direct, peer-to-peer Bitcoin payments
* No transaction fees (other than the [network fee](https://en.bitcoin.it/wiki/Miner_fees))
* No processing fees
* No middleman
* No KYC
* Non-custodial (complete control over the private key)
* Enhanced privacy
* Enhanced security
* Self-hosted
* SegWit support
* Lightning Network support (LND, c-lightning, Eclair and Ptarmigan)
* Tor support
* Opt-in [altcoin](https://docs.btcpayserver.org/FAQ/FAQ-Altcoin/) integrations
* Full compatibility with BitPay API (easy migration)
* Process payments for others
* Easy-embeddable Payment buttons
* Point of sale app
* Crowdfunding app
* Payment Requests
* Internal, full-node reliant wallet with [hardware wallet integration](https://docs.btcpayserver.org/Vault/)

## Getting Started

Firstly, decide if you want to host an instance yourself or use a [third-party host](https://docs.btcpayserver.org/ThirdPartyHosting/). If you've chosen to self-host, we documented plenty [ways to deploy BTCPay Server](https://docs.btcpayserver.org/Deployment/).

After successful deployment, make sure to check our [getting started](https://docs.btcpayserver.org/RegisterAccount/) and [walkthrough](https://docs.btcpayserver.org/Walkthrough/) guides. In case you would like to use Lightning Network, see [Lightning guide](https://docs.btcpayserver.org/LightningNetwork/).

## Documentation

Please check out our [official website](https://btcpayserver.org/), our [complete documentation](https://docs.btcpayserver.org/) and [FAQ](https://docs.btcpayserver.org/FAQ/) for more details.

If you have trouble using BTCPay, consider joining [communities listed on official website](https://btcpayserver.org/#communityCTA) to get help from BTCPay community members. Only file [Github issue](https://github.com/btcpayserver/btcpayserver/issues) for technical issues you can't resolve through other channels or feature requests you've validated with other members of community.

Main community chat is located on [Mattermost](https://chat.btcpayserver.org/).

## Contributing

BTCPay is built and maintained entirely by volunteer contributors around the internet. We welcome and appreciate new contributions.

If you're a developer looking to help, but you're not sure where to begin, check the [good first issue label](https://github.com/btcpayserver/btcpayserver/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22), which contains small pieces of work that have been specifically flagged as being friendly to new contributors.

Contributors looking to do something a bit more challenging, before opening a pull request, please [create an issue](https://github.com/btcpayserver/btcpayserver/issues/new/choose) or join [our community chat](https://chat.btcpayserver.org/) to get early feedback, discuss best ways to tackle the problem and to ensure there is no work duplication.

* [Setting up development environment on Windows](https://www.youtube.com/watch?v=ZePbMPSIvHM)
* [Setting up development environment Linux (Ubuntu)](https://www.youtube.com/watch?v=j486T_Rk-yw&t)
* [Setting up development environment MacOS](https://www.youtube.com/watch?v=GWR_CcMsEV0)

You also have an awesome video of our contributors which explains how to get started.
[![Rockstar Dev and Britt Kelly - BTCPay Server Code Along](https://img.youtube.com/vi/ZePbMPSIvHM/sddefault.jpg)](https://www.youtube.com/embed/VNMnd-dX9Q8)

Here is some info about [how to extend the themes](https://docs.btcpayserver.org/Theme/).

## How to build

While the documentation advises to use docker-compose, you may want to build BTCPay Server yourself.

First install .NET Core SDK v3.1 as specified by [Microsoft website](https://dotnet.microsoft.com/download/dotnet-core/3.1).

On Powershell:

```powershell
.\build.ps1
```

On linux:

```sh
./build.sh
```

## How to run

Use the `run` scripts to run BTCPayServer, this example shows how to print the available command line arguments of BTCPayServer.

On Powershell:

```powershell
.\run.ps1 --help
```

On linux:

```sh
./run.sh --help
```

## How to debug

If you want to debug, use Visual Studio Code or Visual Studio 2019.

You need to run the development time docker-compose as described [in the test guide](./BTCPayServer.Tests/README.md).

You can then run the debugger by using the Launch Profile `Docker-Regtest` on either Visual Studio Code or Visual Studio 2017.

If you need to debug ledger wallet interaction, install the development time certificate with:

```bash
# Install development time certificate in the trust store
dotnet dev-certs https --trust
```

Then use the `Docker-Regtest-https` debug profile.

## Other dependencies

For more information, see the documentation:
[How to deploy a BTCPay server instance](https://docs.btcpayserver.org/Deployment/).

## Supported altcoins

Bitcoin is the only focus of the project and its core developers. However, opt in integrations are present for [several altcoins](https://docs.btcpayserver.org/FAQ/FAQ-Altcoin/). Altcoins are maintained by their respective communities.

## License

BTCPay Server software, logo and designs are provided under [MIT License](https://github.com/btcpayserver/btcpayserver/blob/master/LICENSE).

## Supporters

The BTCPay Server Project is proudly supported by these entities through the [BTCPay Server Foundation](https://foundation.btcpayserver.org/).

<table>
  <tbody>
    <tr>
      <td align="center" valign="middle">
        <a href="https://kraken.com" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/kraken.svg" alt="Kraken" height=100>
          <br/>
          <span>Kraken</span>
        </a>
      </td>
      <td align="center" valign="middle">
        <a href="https://twitter.com/sqcrypto" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/squarecrypto.svg" alt="Square Crypto" height=100>
          <br/>
          <span>Square Crypto</span>
        </a>
      </td>
      <td align="center" valign="middle">
        <a href="https://www.btse.com" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/btse.svg" alt="BTSE" height=100>
          <br/>
          <span>BTSE</span>
        </a>
      </td>
      <td align="center" valign="middle">
        <a href="https://www.dglab.com/en/" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/dglab.svg" alt="DG Lab" height=100>
          <br/>
          <span>DG Lab</span>
        </a>
      </td>
    </tr>
    <tr>
      <td align="center" valign="middle">
        <a href="https://www.okcoin.com/" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/okcoin.svg" alt="OKCoin" height=100>
          <br/>
          <span>OKCoin</span>
        </a>
      </td>
       <td align="center" valign="middle">
        <a href="https://acinq.co/" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/acinq-logo.svg" alt="ACINQ" height=100>
          <br/>
          <span>ACINQ</span>
        </a>
      </td>
      <td align="center" valign="middle">
        <a href="https://lunanode.com" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/lunanode.svg" alt="LunaNode" height=100>
          <br/>
          <span>LunaNode</span>
        </a>
      </td>
      <td align="center" valign="middle">
        <a href="https://walletofsatoshi.com/" target="_blank">
          <img  src="BTCPayServer/wwwroot/img/walletofsatoshi.svg" alt="Wallet of Satoshi" height=100>
          <br/>
          <span>Wallet of Satoshi</span>
        </a>
      </td>
    </tr>
  </tbody>
</table>

If you'd like to support the project, please visit the [donation page](https://btcpayserver.org/donate/).
