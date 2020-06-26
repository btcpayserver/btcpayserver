# Changelog

## 1.0.5.0:

### New Feature and improvements
* Add Notifications feature (@r0ckstardev)
* Add Pull Payments feature (@NicolasDorier)
* Add Refund feature (@NicolasDorier
* Allow invoice and payment requests to be archived (hide from list) (@Kukks)
* Improve fee selection UI in wallet send screen (@Kukks)
* Show warning when enabling Payjoin but supported payment methods are not using a hot wallet (@Kukks)
* Rebuild POS and Crowdfund App Item/Perk editor UI and fix any issues (@Kukks)
* Adjust Payjoin to the new specification outlined in BIP78 (@NicolasDorier) 
* Allow opening the BTCPay wallet natively when clicking a Bitcoin payment link (BIP21)
* Add Server Info GreenField API (@dennisreimann)
* Add Payment Requests GreenField API (@Kukks @NicolasDorier)
* Support specifying payment method through apps per item/perk (@Kukks)
* Allow setting private route hints for LND invoices (@r0ckstardev)
* Expand GreenField Store API to have more store properties (@Kukks @NicolasDorier)
* Make GreenField local docs require authentication (@Kukks)
* Privacy enhancement: Randomize wallet transaction fingerprint. (@NicolasDorier)
* Randomize RBF support in BTCPay wallet by default for privacy (@NicolasDorier)
* Add support for Cobo Vault wallet file import (@Kukks)
* Add support for Wasabi wallet file import (@Kukks)
* Split POS app cart and static and support both simultaneously (@dennisreimann)
* Add Cross-Implementation Lightning Node GreenField API (@Kukks @NicolasDorier)
* Make GreenField responses and JSON properties consistent (@Kukks @NicolasDorier)
* Add Tor Onion-Location HTTP header (@dennisreimann) 
* Rename form items in Wallet Send UI (@pavlenex)
* Add ThunderHub integration (@NicolasDorier)
* Add option to attempt to create PSBT with full transaction for inputs to sign for Trezor (@Kukks)
### Bug fixes

* Do not auto-complete generate wallet form (@Kukks)
* Make sure copied notification is positioned correctly on checkout (@chewsta)
* Fix broken documentation links (@Kukks @dennisreimann @jad0c @Eskyee @pavlenex)
* Fix POS app item display issues (@bolatovumar)
* Fix Invoice checkout modal close button theme issues (@bolatovumar)
* Fix display of replaced transactions in invoice list (@NicolasDorier)
* Support BitPay invoice creation property "paymentCurrencies" (@Kukks)
* Update lightning support warning text (@janoside)
* Fix issue with payment requests not expiring (@Kukks)
* Fix broken Bootstrap theme generator link (@Kukks)
* Use proper Bitcoin symbol (₿) in UI instead of "BTC" (@Kukks)
* Ensure you can only sign with hot wallet if you created the hot wallet via seed (@NicolasDorier)
* Respect JsonResponse option in payment button endpoint even for failures (@Kukks)
* Opt out of dotnet telemetry in Docker containers (@Kukks)
* Fix issue with POS app if button text had special formatting (@Kukks)
* Autofocus 2FA-code input on page load (@dennisreimann)
* Add Orderid to BitPay IPN format (@Kukks)
* Require Owner role to the store for modifying store via Greenfield (@NicolasDorier)
* Fix css styling classes (@woutersamaey)
* Fix checkout high width resolution styling issues (@dennisreimann)
* Fix zpub electrum import (@gruve-p)
## 1.0.4.4:

### New Feature

* Allow user to select different fee rate based on expected confirmation time (@NicolasDorier)

### Bug fixes

* Fix QR Code on dark theme by adding some white margin around it (@chewsta)
* Make sure wallet support decimal fee ... again. (@NicolasDorier)

## 1.0.4.3:

### New features

* If you use a hot wallet, you can retrieve the seed in wallet settings / Other actions / View seed (@kukks)
* Add top Label filter (@kukks)
* As a sender, payjoin transaction are tagged in the wallet (@kukks)

### Bug fixes

* The wallet now discourage fee sniping (increase privacy by mimicking wallets like bitcoin core) (@NicolasDorier)
* Payjoin receiver fix: The receiver's inputs sequence must be the same as the sender's inputs' sequence (@NicolasDorier, reported by @waxwing)
* The wallet do not round fee rate to the nearest integer. (@NicolasDorier)
* Invoice row should not cut off the "AM/PM" part of the date (@r0ckstardev)
* Ensure dropdown in checkout page does not overflow (@ubolator)
* Fix decimal points shown in Checkout UI based on currency ( always showed btc decimal precision before) (@kukks #1529)
* fix label link inconsistency (@kukks)
* Fix payjoin detection in checkout UI (@kukks)

### Altcoins
* For liquid, fix decimal precision issue in the wallet (@kukks)
* For liquid, the transactions in a wallet of a specific asset should only show transactions specific to this asset (@kukks)

### Language
* Update portuguese strings (@BitcoinHeiro)

## 1.0.4.2

### New feature and improvements
* Auto labelling of wallet transactions, for now three labels "invoice", "pj-exposed", "payjoin" (@kukks)
* Checkout dark theme improvements (@dennisreimann #1508)
* Show warning when create a hot wallet when you are not admin of the server (@kukks)
* In store settings, shows "Not set" if a derivation scheme is not set. If it is set, always show the last few letters of the derivation scheme. (@kukks)
* Do not show lightning network configuration for Liquid assets. (@kukks)
* Better UTXO selection for payjoin receiver (@kukks #1470)
* Payjoin: But the automatic broadcast of original transaction from 1 minute to 2 minutes. (to give more time to sign with a hardware wallet)
* Greenfield: Expose an health check endpoint without authentication (@dennisreimann)
* Greenfield: Very primitive create/read/update/delete store endpoints (@kukks)

### Bug fixes
* With LND above 0.9, invoices were immediately transitioning as partially paid. (@r0ckstardev)
* Successful payjoin in P2SH-P2WPKH would result in overpaid invoice (@kukks)
* If payjoin sender is sending the PSBT in hex format, we should send back the proposal in hex format (@kukks)
* Payment request were redirecting to non-existing (404) URL after payment (@kukks)
* Incorrect derivation scheme in generate wallet were giving an error 500 instead of proper error message (@kukks)
* When pasting a BIP21 when using coin selection, it would throw an error. (@kukks)
* In the Wallet Send page, remove a JS script reference which does not exist anymore. (@kukks)
* Fix LCAD logo (@dennisreimann)
* Fix dark theme contrast for Payment Requests (@ubolator and @dennisreimann #1488)
* Fix MySql supports details (@ketominer)
* In dark theme, the pay button was rendering BTCPAY text in black. (@dennisreimann #1517)

### Miscalleneous
* Refactor CSS to be in line with [the new design system](https://design.btcpayserver.org/views/bootstrap/) (@dennisreimann)
* Tests utilities: Fix docker-lightning-cli scripts
* Improve static asset caching (@dennisreimann)
* New invoice checkout languages added:**
  * Bulgarian (Bulgaria) (bg_BG) @doynovbps
  * Danish (Denmark) (da_DK) @Berlelund
  * Norwegian (no) [@devenia](https://www.transifex.com/user/profile/devenia/)
  * Persian (fa) [@firildakh](https://www.transifex.com/user/profile/firildakh/)
  * Romanian (ro) [@BTCfactura](https://www.transifex.com/user/profile/BTCfactura/)
  * Slovak (Slovakia) (sk_SK) [@MSedivy](https://www.transifex.com/user/profile/MSedivy/)
  * Zulu (zu) [@kpangako](https://www.transifex.com/user/profile/kpangako/)
* Updated translation for checkout invoice:**
  * Arabic (Ar) @kemoantemo
  * Bosnian (Bosnia and Herzegovina) (bs_BA) @Ruxiol
  * Danish (Denmark) (da_DK) @Berlelund
  * German (Germany) (de_DE)[@andhans](https://twitter.com/andhans_jail)
  * Greek (Greece) (el_GR) @kaloudis
  * Spanish (Spain) (es_ES) @RzeroD
  * Hindi(hi) @blockbitmedia
  * Indonesian (id) @anditto
  * Polish (pl) [@kodxana](https://www.transifex.com/user/profile/kodxana/)
  * Portuguese (Pt_pt) [MarcosMe](@https://www.transifex.com/user/profile/MarcosMe/)
  * Turkish (tr) [efecini](https://www.transifex.com/user/profile/efecini/)


## 1.0.4.1

### Bug fixes
* Payjoin not working correctly for P2SH-P2WPKH merchants. @kukks

* Clicking on the balance amount on send wallet, was not checking "Substract fees" automatically @kukks

## 1.0.4.0

Since this release is substantial, we invite your to read our [blog post](https://blog.btcpayserver.org/btcpay-server-1-0-4-0/) as well.

### Bug fixes
* Better RBF and Double spend handling
    * Fix: Bumping an invoice payment would sometimes add to the customer Network fee.
    * Fix: A double spent transaction would sometimes show as never confirming in the invoice details instead of showing as double spent
* Fix: do not allow 0 amount invoices in crowdfund or payment requests
* Fix: Make 0 amount invoices marked as paid instantly
* Fix: Payment request clone button would throw an error
* Fix: Could not remove a user if the user was using the storage file feature
* Make sure sponsor logos show up nicely on all screen sizes
* UI Fixes
    * Replace `Paid summary` by `Invoice Summary` in the invoice preview of the invoice list page
    * Center supporter logos on the 404 error page
    * When creating a new hotwallet, do not ask for the address confirmation step


### Features
* Payjoin support for stores (Receiving)
* Payjoin support in the internal wallet (Sending)
* Coin Selection feature in the internal wallet
* Direct integration to Bitflyer rate provider
* Allow generation of new address in Wallet Receive page, even if the current one still not used.
* New invoice default theme
* New invoice dark theme
* New site default theme
* New site dark theme
* Camera QR Code scanner for Wallet
* In the invoice checkout, ability to copy the BIP21 payment string
* Add additional server policy for hot wallet RPC import

### Greenfield API
* Greenfield API Permissions rework for API Keys & Basic Auth support
   * Granular permissions
   * Endpoint for creating a new user
   * Endpoint for creating API Keys
   * More details in the documentation
* Greenfield API C# Client

### Altcoins

* Decimal precision for Liquid assets fixes
* Add L-CAD support for Liquid
* Monero stability fixes

## Thanks to contributors

* binarydreaming
* britttttkelly
* dennisreimann
* francispoulios
* joerlop
* mbomb1231
* mikewchan
* mrkukks
* nicolasdorier
* pavlenex
* rockstardev
* ubolator
* vswee
