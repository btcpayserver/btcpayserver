# Changelog

## 1.8.1

### New feature

* Add dropdown language selector in checkout v2 @dennisreimann

### Bug fix

* Avoid crash when some plugins are installed (#4725)
* Greenfield: Do not create if create API key is called on a non-existant user (Fix #4731)

### Improvements

* Remove superflous punctuation in some translations
* Update Polski translation

## 1.8.0

Bear markets are for building: This version brings custom checkout forms, store branding options, a redesigned Point of Sale keypad view, new notification icons and address labeling.

Read more information in [v1.8.0 blog's post](https://blog.btcpayserver.org/btcpay-server-1-8-0/).

### New feature

* Generic Forms (#4561 #4668 #4697) @Kukks @dennisreimann
* Add labels to addresses (#4594) @Kukks
* Greenfield: Admins can create/delete API keys of any user (#4680) @NicolasDorier

### Bug fix

* Fix build and run scripts (#4655) @NicolasDorier
* Fix missing style tag around embedded CSS (#4659) @dennisreimann
* Fix crash during migration on some SQLite instances (#4623) @NicolasDorier
* Fix HTML injection in payment request/posData/receiptData (#4679) @NicolasDorier
* Show available plugins even when btcpay version conditions are not met (#4717) @Kukks
* Fix: It wasn't possible to use Point of Sale in an iframe if forms was asked to buyer (#4666 #4721) @dennisreimann @NicolasDorier

### Improvements

* Different icons for notifications (#2510) @dstrukt @dennisreimann
* POS: Track values (subtotal, discount, tip, total) individually (#4668) @dennisreimann
* Start using JSONB column instead of app side compressed data (#4574) @NicolasDorier
* Update transaction label display (#4700) @dennisreimann
* Remove JSON in strings from JObjects (#4703) @NicolasDorier
* Do not require docker for plugin restart @Kukks
* POS: Align Keypad centered vertically (#4690) @dennisreimann
* Greenfield: Show detailed Lightning routing error (#4722) @dennisreimann
* Add currency code to payment request list (#4709) @bolatovumar
* Translate Checkout v2 (#4710) @NicolasDorier

## 1.7.12

Update recommended for shared instances.

### Bug fixes

* Fix second order XSS: Harden file type input (#4635) @NicolasDorier
* UI: The standalone confirmation modal design was broken @dennisreimann
* Fix: Setting the password of a new created user via API shouldn't be required (#4534, #4647) @NicolasDorier
* Fix: If user get locked out, unlocking or deleting user fails (#4641, #4644) @NicolasDorier
* Fix: Migrating from SQLite was crashing in some conditions (#4623) @NicolasDorier
* Fix: Unable to Edit amount when cloning paid Payment Request (#4639) @NicolasDorier
* Webhook: Add missing model validation (#4636) @dennisreimann
* Checkout v2: Fix automatic redirect after paid (#4633) @dennisreimann

### Improvement

* Preferred paging count is saved into user preferences cookie (#4637) @dennisreimann
* Fix typo in error message when vault is opening a wallet from the wrong chain (#4640) @eltociear

## 1.7.11

### Improvement

* Better descriptions of some feature in the UI (#3831) @dstrukt @dennisreimann

### Bug fix

* Fix XSS on public instances #4629 (Credit to @d47sec) @NicolasDorier
* Fix an exception happening on some app with mapped dommain (#4622) @dennisreimann
* Fix error thrown in the pairing flow to woocommerce or other external apps (#4627 #4630) @Kukks
* Fix HTML appearing in pull payment's LN Url descriptions (#4624 #4630) @Kukks

## 1.7.10

### Bug fix

* After successful migration from SQLite or MySql, there is an error after a restart @NicolasDorier

## 1.7.9

### Bug fixes

* Fix: Top-Up Invoices display fiat amounts to 9 decimal places in emails (#4570) @Kukks
* LNURL NFC support did not work for lower amount invoices (#4618) @Kukks

## 1.7.8

With this release, we are providing a migration path for legacy MySql and SQLite installations.

If you are a BTCPay Server integrators such as developer of Raspiblitz, Umbrel, Embassy OS or anybody running BTCPay Server on SQLite or MySql, please refer to [the documentation](docs/db-migration.md).

While SQLite and MySQL should still be working for one year or two, we will not fix bugs related to those backend. (unless it impacts migration)

### New feature

* Add ability to migrate from MySQL/SQLite to Postgres backend. (#4614) Please read [the documentation](docs/db-migration.md). @NicolasDorier

### Bug fixes

* Fix: MySQL install were crashing during db update @NicolasDorier
* In case of the unified invoice, the LNURL wasn't correct (#4616, #4609) @dennisreimann
* Fixes missing uppercasing for the QR code in case of non-unified QR. @dennisreimann
* Fix: snort.social still didn't play with our lnaddress implementation (#4615, #4617) @dennisreimann

## 1.7.7

Some users experienced Error 500 after login on to BTCPay Server from the 1.7.6.
If it is your case, to update on docker deployments via the UI, you need to:

1. Start a browser session in incognito/private mode.
2. Browse to `https://{yourserver}/server/maintenance`
3. Hit update button

### Bug fixes

* Fix crash for installations supporting zcash or monero (#4610) @NicolasDorier

## 1.7.6

There are two vulnerabilities fixed in this release.
Those are not severe, as it requires the victim to actively click on a malicious link, but we recommend to update.

We also introduce a breaking change in the Greenfield API route `/api/v1/stores/{storeId}/rates/configuration/preview`. (#4607)
This breaking change shouldn't impact the majority of people.

### New features

* Make Lightning NFC built in (#4541) @Kukks
* Greenfield get app details (#4102) @bolatovumar
* Greenfield: Add store rates api (#4550) @Kukks
* Server Theme: Allow to unset CustomThemeCssUri @dennisreimann
* Store Branding: Add custom CSS option (#4459, #4527) @dennisreimann
* Store branding on invoice and receipts, payment requests and pull payments, point of sale and crowdfunding (#3842, #4568) @dennisreimann
* Add Greenfield API endpoint for pull payment LNURL items (#4472) @bolatovumar
* Greenfield: Add lightning payments list endpoint (#4407) @dennisreimann
* Add additional permission for pull payments (#4539) @Kukks

### Bug fixes

* Fix: Mark selected payouts as already paid had an unexpected result (#4579) @Kukks
* Fix: Payjoin wasn't always properly choosing utxo for optimal change (#4600) @NicolasDorier
* Fix: If PoS item code contains a /, LNUrl would not work (#4601, #4602) @NicolasDorier
* Fix: a bunch of open redirect (#4575). Credit to @gonzxph. @NicolasDorier
* Fix: Disqus integration in Crowdfunding store (#4580, #4572) @dennisreimann
* Fix: XSS on uploaded files to the file storage (#4567) Credit to @ctflearner. @NicolasDorier
* Fix: Greenfield currency rate should be strings (#4607) @NicolasDorier

### Improvements

* If a domain name is mapped to an app, always redirect the ugly /apps/{appId} to it (#4391) @dennisreimann
* Add missing CORS to LN Address/LNUrl route (Compatibility with Beach Wallet) (#4587) @NicolasDorier
* Make plugin able to register rate providers (#4551) @NicolasDorier
* Point of Sale: Improve merchant view (#4560) @dennisreimann

## 1.7.5

### New features

* Greenfield: Lightning addresses API (#4546) @kukks

### Bug fixes

* Fix several HTML injections (#4545) @NicolasDorier
* BIP21: Uppercase addresses only in QR, not in payment URL (#4553) @dennisreimann
* Checkout v2: UI fixes (#4552) @dennisreimann

### Improvement

* Checkout v2: Re-add LNURL for top-up invoices (#4556) @dennisreimann

## 1.7.4

Note for integrators such as Raspiblitz or Umbrel: As part of our effort to make BTCPay Server more welcoming to plugins, we have made a change that may impact you.

Previously, when a user uninstalled or installed a new plugin, BTCPay Server would prompt them to restart the server by clicking on a button. Prior to version 1.7.4, this restart button was not functional due to being coupled to our own Docker deployment stack.

As of now, the restart button will instead terminate the BTCPay Server process. The process manager, such as systemd or docker should then automatically restart BTCPay Server. Please ensure that automatic restart capability exists.

### Bug fixes

* Fix LNURL authentication as 2FA method (#4501) @dennisreimann
* Fix sync modal display (#4485) @dennisreimann
* Checkout: Fix cutoff language dropdown (#4486) @dennisreimann
* Point of Sale: Ensure only valid non-negative numbers in tip input (#4481) @bolatovumar
* Invoice export: Fix export all stores case (#4516) @dennisreimann

### Improvement

* After a plugin install or uninstall, restart now just kill the process instead of requiring SSH with docker install @NicolasDorier
* Checkout v2: Display and copy addresses (#4489) @dennisreimann
* Checkout v2: Configure countdown timer (#4471) @dennisreimann
* Unify 2FA login boxes (#4506) @dennisreimann
* Add extension points for dashboard (#4483) @kukks
* Text editor: Allow Twitter embeds (#4488) @dennisreimann
* Update preferred price source copy label (#4492) @dstrukt
* Display BTCPay Server version numbers in UI without zero suffix (#4521) @NicolasDorier
* Redesign plugin list items (#4528) @NicolasDorier
* Plugin development improvements (#4522 #4518) @NicolasDorier
* Greenfield: Add payment hash and preimage to Lightning invoices (#4520) @dennisreimann
* Greenfield: Add separate permission for viewing LN invoices (#4529) @ArttuPakarinen @dennisreimann

## 1.7.3

### Bug fixes

* Checkout v2: Fix modal iframe clipboard permissions (#4453) @dennisreimann
* Checkout: Fix cutoff language dropdown (#4465) @cdmoss
* Fix a crash on coin selection if we weren't able to guess the color of a label (053426) @kukks
* If using LNBank, LN invoices are not generated after upgrade to v1.7.2.0. You still need to also update the plugin. (#4458) @dennisreimann
* Fix BTCPay invoice not settling after successful Lightning payment (CLN + Lightning Charge) (#4383) @dennisreimann

### Improvement

* Make sure payment request print view doesn't show table header twice (#4447) @bolatovumar
* Automated payout processors shouldn't spam logs on shutdown (#4193) @NicolasDorier
* Checkout v2: Reduce Altcoin name on payment method pill (#4456) @dennisreimann
* Checkout: Make CSS and logo paths relative (#4354) @bolatovumar
* Checkout: Add persian language back (#4457) @NicolasDorier
* Frontend cleanups (#4449 #4463 #4473) @dennisreimann

## 1.7.2

### New features

* Greenfield: Add `DescriptionHashOnly` to Lightning invoice creation endpoint (#4411) @NicolasDorier
* Greenfield: Breaking change, `DescriptionHash` in the Lightning invoice creation endpoint has been removed (#4411) @NicolasDorier
* UI: Theme extensions (#4398) @dennisreimann

### Bug fixes

* Fix Output Descriptor parsing for WSH multisig case (#4402) @dennisreimann
* Greenfield: Fix lock user call return code and docs (#4377) @dennisreimann
* UI: Fix store selector transition (#4400) @dennisreimann
* PayButton: Fix CSP problems in Firefox (#4376) @dennisreimann
* Fix bitbank and yadio rate providers (#4432) @NicolasDorier
* Plugins built with newer version of BTCPay couldn't run on older version (#4441) @NicolasDorier

### Improvement

* Adapt LN payouts to handle unknown status (#4382) @Kukks
* Upgrade Bootstrap to v5.2.3; Design System improvements (#4380 #4409) @dennisreimann
* Wallet import: Surface detailed error messages (#4392) @dennisreimann
* Local file system storage as default (#4386) @dennisreimann
* Remove system plugins from the plugin list (#4429) @NicolasDorier
* Allow more then 20 accounts when using BTCPayServer.Vault (#4430) @dennisreimann
* Add BTCTurk rate provider (#4433) @NicolasDorier
* Rate provider: Use better default than Coingecko when creating a new store (#4416) @NicolasDorier
* Add DefaultDescription to LNURL withdrawal request (#4434) @bolatovumar
* Disabled amount/currency update for payment request with active invoices (#4390) @bolatovumar
* Add store logo to invoice receipt page (#4435) @bolatovumar
* Add links to docs and API in the footer (#4431) @NicolasDorier

### Miscellaneous

* BTCPay Server will work on Core Lightning 22.11 outside of the Docker deployment
* If running Core Lightning 22.11 outside of the Docker deployment, you don't need the plugin `invoicewithdescriptionhash` anymore
* Added support for running Core Lightning with `allow-deprecated-apis=false`

## 1.7.1

### New features

* Greenfield: API for create an invoice refund (#4238, #4181) @bolatovumar
* Greenfield: Add metadata to invoice webhook event (#4370, #4223) @bolatovumar

### Bug fixes

* Fix error HTTP 500 happening on Point of Sale (#4355, #4368) @NicolasDorier
* Some logos or images wouldn't show up properly if rootPath is used (#4367) @NicolasDorier
* Fix malformed manifest on PoS app (#4373, #4363) @dennisreimann
* Call to LND would start failing on some conditions @NicolasDorier
* Checkout v2: Fix for BIP21 case with default payment method other than onchain BTC (#4375) @dennisreimann

### Improvement

* Adjust currency name to be standard (#4369) @bolatovumar
* Language update in checkout v1 for pt-BR and sv cultures

### Miscellaneous

* Mark MySql and SQLite backend deprecated

## 1.7.0

### New features

* New version of the checkout as opt-in feature (#4157 #4276 #4345) @dennisreimann @dstrukt
* Request customer data with forms for email and shipping address (#4137) @Kukks
* Server settings: Add option to customize the instance logo (#4258) @dennisreimann
* Store settings: Add branding options (#4131) @dennisreimann
* Greenfield: Store Rates Config (#3931) @Kukks
* Greenfield: Get Lightning invoices (#4180) @dennisreimann
* Greenfield: Create payment request invoices (#4243) @NicolasDorier
* Greenfield: Allow marking payout status and payment proofs (#4244) @Kukks
* Greenfield: Wallet Objects (#4274 #4297) @Kukks @NicolasDorier
* Greenfield: Add crowdfund app create endpoint (#4068) @bolatovumar
* Add Lightning Service Torq (#4296) @maxwedwards

### Bug fixes

* Do not crash startup if ssh settings aren't correct (0286c7) @NicolasDorier
* UI: Fix missing timezone in browser dates (#4210) @dennisreimann
* PayjoinController could throw HTTP 500 in corner cases (#4215) @NicolasDorier
* Greenfield: The route to connect to a peer lightning node was always crashing (8b7921) @NicolasDorier
* Greenfield: Fix missing payment data (#4233) @dennisreimann
* Invoice's orderId equals to payreq id shouldn't appear part of the pay request (9e7326) @NicolasDorier
* Fix Public Node Info View for IPv6 addresses (#4247) @dennisreimann
* Confirm modal: Prevent form submit without confirmation (#4262) @dennisreimann
* Do not activate onchain payment method if node is unavailable (#4291) @Kukks
* Receipts: Fix amount paid discrepancy (#4287) @dennisreimann
* Minor UI fixes (#4209 #4221 #4232 #4253 #4311) @dennisreimann
* Show error message if reverse proxy domain isn't well configured (#4322) @NicolasDorier
* Update incorrect `monitoringExpiration` field for invoice API docs (#4348) @bolatovumar

### Improvement

* Refactor labels (#4179 #4297 #4347) @NicolasDorier
* Allow config to set default block explorer link (#4249) @Kukks
* Import xpub: Surface error details (#4205) @dennisreimann
* Sync modal improvements (#4260) @dennisreimann
* Remove asset bundle minifier (#4222 #4225) @NicolasDorier @dennisreimann
* Vault: Add warnings for Safari and Brave (#4226) @NicolasDorier
* Greenfield: Graceful return for in-flight HTLCs (#4252) @dennisreimann
* Greenfield: Docs improvements (#4231 #4235) @NicolasDorier @dennisreimann
* Add donate link to footer(#4239) @dennisreimann
* Improve access token pairing (#4237) @dennisreimann
* Lightning: Better handling for non-public nodes (#4263) @dennisreimann
* Use PluginLoader in the PluginPacker to prevent conflicts (#4277) @NicolasDorier
* Remove dead shitcoin MUE (c8a102) @NicolasDorier
* Unify payment request list with invoices (#4294) @dennisreimann
* POS: Validate cart cost with explicit amount (#4307) @Kukks
* Disable internal node options if no internal node configured (#4315) @NicolasDorier
* Use the plugin builder website instead of docker to fetch plugins (#4285) @NicolasDorier
* Update Code analysis (#4293) @JesterHodl
* Server Settings: Update Policies page (#4326) @dennisreimann
* Wallet Settings: Update speed policy wording (#4328) @ndeet
* Plugins: Add documentation link (#4329) @NicolasDorier
* Show the git commit of the current build of BTCPay (#4320) @NicolasDorier
* Disable receipts for payment request invoices (#4342) @Kukks

## 1.6.12

### New features

* Greenfield: Extend LN GetInfo data (#4167) @dennisreimann

### Bug fixes

* Always show overpaid amount if invoice is overpaid (#4192) @bolatovumar
* Fix custodian Swagger docs missing some path parameters (#4196) @AryanJ-NYC
* Fix receipts for Lightning Address invoices (#4169) @dennisreimann
* POS: Fix keypad view without custom amount (#4183) @dennisreimann @bolatovumar
* Fix truncated dates in wallet transaction list (#4191) @dennisreimann
* Update default value for "showCustomAmount" in Swagger docs (#4200) @bolatovumar

### Improvement

* The wallet transaction list use infinity scroll rather than pagination (#4074) @HamroRamro @dennisreimann
* Remove direct and temp link functionality from the File Storage (#4177) @daviogg
* Fix warning error when rebooting the server caused by some shitcoin currency pair format @NicolasDorier
* Add Invoice.OrderId to list of supported email interpolation strings (#4202) @bolatovumar
* Do not hide errors happening in tasks spawned by BaseAsyncService @NicolasDorier

## 1.6.11

### New feature

* Add support for updating POS app through Greenfield API (#3458) @bolatovumar
* Allow specifing fee block target for onchain payout processor (#4065) @Kukks

### Improvement

* Make POS and Crowdfund system plugins (#3987) @dennisreimann
* Enhance export function for invoices (#4060) @HamroRamro
* Create dynamic manifest for pos apps (#4064) @Kukks
* Update invoice amount description in Swagger template @bolatovumar
* Improve payout processors description (#4109) @woutersamaey
* Set explicit cursor style property on pay button with custom text (#4107) @bolatovumar
* Use mempool space as default block explorer (#4100) @junderw
* Improve Lightning Node setup examples (#4033) @dennisreimann
* Refactor QR functionality and improve wallet import support (#4091) @dennisreimann
* Sanitize filename for downloaded files (#4117) @dennisreimann
* Improve PayButton error page (#4129) @dennisreimann
* Consistent switch UI on Create Wallet views (#4135) @dennisreimann
* Point Of Sale: Custom amount disabled by default (#4126) @daviogg
* Improve "Advanced Settings" button (#4140) @dennisreimann
* Improve currency selection (#4155) @dennisreimann
* Add official Telegram link to footer (#4160) @daviogg
* Minor updates to security issues and bug reports doc (#4161) @Bangalisch

### Bug fixes

* Fix app-related API docs @bolatovumar
* Fix missing route hints option for LNURL invoices (#4077) @dennisreimann
* Scale-down PoS item image (#4076) @prusnak
* Ensure apps can be deleted through UI (#4080) @bolatovumar
* Make sure end date is after start date in Crowdfund app (#4084) @bolatovumar
* Show iframe when showing invoice in Shopify plugin (#4106) @bolatovumar
* LNURL max value is set to min when item type is minmum (#4115) @Kukks
* Fix Monero and Zcash nav extensions (#4124) @dennisreimann
* Add missing store ID to invoice links (#4128) @bolatovumar
* Fix pagination of wallet's transactions (#4150) @dennisreimann @NicolasDorier
* Remove redundant exception status from invoice state label (#4151) @bolatovumar
* Fix Store Settings nav highlight (#4138) @daviogg
* Fix crash on migration from old install (#4162) @NicolasDorier

## 1.6.10

### Bug fixes:

* Fix Wallet Transactions screen not loading in case of older payout labels (#4078) @Kukks
* Fix missing route hints for LNURL (#4077) @dennisreimann
* Fix API Docs url (#4061) @dennisreimann
* Fix Pay button logo and url (#4054) @dennisreimann

## 1.6.9

### Bug fixes:

* Revert #4031 @Kukks

## 1.6.8

### New feature:

* Edit Pull Payment UI (#4016) @daviogg
* Custodian Account Deposit UI (#4024) @woutersamaey
* Add Yadio rate provider (#4048) @bolatovumar

### Improvement:

* Add no rate found error message in Invoices (#4039) @HamroRamro
* Greenfield API docs improvements (#4041 #4035) @dennisreimann
* Allow binding ip and port for non ssl scenarios too (#4031) @Kukks

### Bug fixes:

* Fix crash when running BTCPay without BTC (#4038) @Kukks
* Fix edge cases around LNURL not providing invoice (#4034) @dennisreimann
* Fix store creation button distortion  (#4025) @bolatovumar
* Fix payout labels (#4032) @Kukks
* Handle hold invoices in ln payouts (#4032) @Kukks
* Save preimage of ln payouts when possible (#4032) @Kukks
* Fix crash on stores that had configured a payment method that is no longer supported (#4042) @Kukks

## 1.6.7

### Improvement:

* Improve LNDHub support @dennisreimann

### Bug fixes:

* Fix Kraken rate source (#4027) @Kukks

## 1.6.6

### Bug fixes:

* Ensure payout processors update state @Kukks

## 1.6.5

### Bug fixes:

* Fix crash when running BTCPay without BTC (#4017) @NicolasDorier

## 1.6.4

### Improvement:

* If a webhook is the loopback address, skip SSL verification @Kukks

### Bug fixes:

* Minor checkout UI fix (#4015) @dennisreimann
* Greenfield: Update webhook would reset the secret (#4010) @Kukks
* Fix crash when BTC network isn't available (#4007) @Kukks
* Make InvoicePaymentSettled return correct afterExpiration value (#3966) @Kukks
* Greenfield: Allow tagging a transaction even if it hasn't been yet broadcasted @Kukks

### Improvements:

* The invoice filter dropdown list labels should be "Settled invoice" rather than "Paid invoice" (#4000 #3573) @BitcoinABee

## 1.6.3

### New feature:

* Point of Sale: Add discount option for Keypad view (#3991) @bolatovumar

### Bug fixes:

* When a payjoin label was applied, coin selection filter would not work (#3986) @Kukks
* When a payjoin happened with a receive address wallet, the payjoin label was not applied (#3986) @Kukks
* Crowdfund: Show free when invoice is fixed and 0 amount in apps (#3994) @Kukks
* Crowdfund: Animations on crowdfund not enabled broke JS (#3994) @Kukks
* Crowdfund: Perk expansion in crowdfund was broken (#3994) @Kukks
* Redirect instead of show 404 on 0 amount invoices (#3904) @Kukks

### Improvements:

* A few design brush up @dennisreimann @dstrukt
* Coin Selection: Fix responsive display (#3992, #3985) @dennisreimann
* Point of Sale: In the receipt, the Order Id is now a link pointing to the point of sale (#3995) @Kukks

## 1.6.2

### Bug fixes:

* Fix: Cannot withdraw refund (payouts) with LNURL (#3953 #3953) @Kukks
* Fix: Cannot withdraw refund (payouts) with LN Address (#3953 #3960) @Kukks
* Fix: Missing pager in the wallet transaction list @NicolasDorier
* Fix: webhook "Send specific events" display issue (#3959) @rustywave

### Improvements:

* Added HRF and Strike to the list of supporters #3965 @dennisreimann
* Invoice summary: Fix indentation and heading levels (#3956) @dennisreimann

## 1.6.1

This fix a critical issue introduced by 1.6.0.
If you are using altcoins integration, you need to update urgently as some change rate may be inverted for some pairs.

### Bug fixes:

* Fix stack overflow if ripio rate provider is unavailable @NicolasDorier
* Fix: For some asset pair the kraken rate was inverted (#3957) @NicolasDorier

## 1.6.0

In the past six months, we fixed a critical security vulnerability in one of BTCPay's versions. The security vulnerability has been disclosed responsibly, and we granted a bounty to the security researcher who discovered it. As far as we know, this particular vulnerability has not been exploited in the wild as it depends on multiple factors. For security reasons, we will not publicly disclose details yet. Timeframe for public disclosure is 6-12 months. We already have a CVE number reserved for it.

It's very likely that by updating BTCPay Server in the past six months, you've already patched this vulnerability. To be safe, update your instance if you haven't done so in a long time.

### New features:

* Dashboard: Add Lightning balances and easy access to lightning services (#3838) @dennisreimann
* Dashboard: Add Point Of Sale data (#3897) @dennisreimann @dstrukt
* Greenfield: Basic API Get and Delete operations for apps (#3894) @bolatovumar
* Greenfield: Add Lightning balance endpoint (#3887) @dennisreimann
* Greenfield: Allow excluding unconfirmed UTXOs when creating a new transaction (#3737) @bolatovumar
* Checkout: Public invoice's receipt (#3612) @Kukks
* Can disable TLS certificate check for email servers @NicolasDorier
* Can add sender's name to any field accepting an email destination, for example `Nicolas Dorier <blah@example.com>` rather than just `blah@example.com` (#3891) @NicolasDorier
* Support LNURL Withdraw in payouts (#3709) @Kukks
* Can send parametized emails based on invoice events (#3611) @Kukks
* Dashboard: Added toggle button to switch to store default currency (#3752) @SakshamSolanki @dennisreimann
* Support Lightning node connection string with onion addresses (#3845) @Kukks
* New rate provider: BUDA a chilean exchange (#3766) @Kukks
* Add Refunds list to Invoice details page (#3815) @Kukks

### Bug fixes:

* UI: Fix cancel plugin command (#3903) @dennisreimann
* Crowdfunding: Fix the links for the default Quake sounds (#3745) @dennisreimann
* UI: Fix nav height issue on mobile devices (#3888) @bolatovumar
* UI: Fix mark all notifications as seen return URL (#3848) @dennisreimann
* UI: When disabling a user, then it as an admin, attempting to remove or enable the user would fail while showing success in the UI (#3829 #3832) @rustywave
* Deleting an admin gives a 500 error, and cannot disable the same user (#3785 #3818) @rustywave
* Fix some rate providers (#3813) @Kukks
* Dashboard: Do not display archived invoices in recent invoices (#3783) @dennisreimann
* Error happening when broadcasting transactions weren't shown in the UI @NicolasDorier
* If LNURL for standard invoice was disabled, and PoS print view used, the QR code would throw error 404 (#3930) @Kukks

### Improvements:

* Don't show "Set up a Lightning node" when LN is not supported (#3935) @bolatovumar
* Redirect to invoice details instead of list upon creation (#3936) @bolatovumar
* Better UI/UX for on-chain send and receive (#3921) @dennisreimann @dstrukt
* Add refund badge to invoice lists (#3918) @Kukks
* Creating and editing a payment request now redirect to the payment request list (#3825) @rustywave
* Crowdfunding: Several UI/UX improvement for the settings (#3708 #3488) @dennisreimann @dstrukt
* Improve the refund flow (#3715 #3731) @dstrukt @dennisreimann
* Improve email settings validation and UX (#3891) @NicolasDorier
* Add spam rate limits for public invoice endpoints (#3782 #3889) @NicolasDorier
* Greenfield doc: Adding description to `speedPolicy` parameter (#3877) @ndeet
* UI: Improvement of Crowdfund & PoS Modal (#3806) @dstrukt @dennisreimann
* Server Settings: Consolidate Storage and Files (#3863) @dennisreimann
* Move `View` action to the `Name` column in Payouts & Payment Requests (#3873) @dstrukt @dennisreimann
* UI: Properly report Shopify errors when testing new settings (#3853) @NicolasDorier
* Mobile header improvements (#3826) @dennisreimann
* Notification modal improvements (#3784) @dstrukt
* Improve payouts UI (#3792) @dstrukt @dennisreimann
* Update language to explicitly request view-only wallet files (b1f62f74cde09d124fe308f5af9e255522add288) @sethforprivacy
* Open public app views in new tab/window (#3920) @dennisreimann

## 1.5.4

### New features:

* Allow resending verification email for users (#3726) @bolatovumar

### Bug fixes:

* Allow pull payments denominated in SATS to be claimed (#3778) @dennisreimann
* Balance was not updated after a wallet rescan @NicolasDorier

## 1.5.3

### New features:

* Add an experimental mode for new features (#3772) @NicolasDorier
* Wallet transactions export (#3744) @dennisreimann

### Bug fixes:

* Fix: Invoices from shopify had empty orderId (#3777 #3769) @NicolasDorier
* Lightning: Allow specifying explicit amount for invoices (#3753) @dennisreimann
* Make login and password not required for sending email (#3764) @bolatovumar @NicolasDorier
* Fix labels not showing multiple payouts payments (#3729) @Kukks
* Fix overflowing text in order ID field in invoices table (#3765) @bolatovumar
* Fix a couple of mobile display issues (#3759) @bolatovumar
* Fix unwanted alert list marker for single error in payout's validation form, issue #3583 MrPaz
* Fix a bunch of open redirect vulnerability @NicolasDorier (Thanks to Iman Sharafaldin @ImanOracle for reporting those)
* Fix Automatically Approved column in pull payment was always false, even if automatic approval was activated (#3693) @bolatovumar

### Improvements:

* Performance improvement when lot's of invoices are pending (#3774) @NicolasDorier
* Remove of an unused table in postgres (#3773) @NicolasDorier
* Remove some logs from the payout processor @NicolasDorier
* Payout Processors: Use friendly name in delete confirmation (#3758) @dennisreimann
* Wallet display improvements (#3755) @dennisreimann
* Some UI improvements @dstrukt

## 1.5.2

### Bug fixes:

* Various internal fixes @NicolasDorier @Kukks
* Various UI fixes (#3702 #3721) @dennisreimann
* Updated Payout processor Label for setting interval (#3698) @Bangalisch
* Update validation of crowdfund app settings (#3708) @bolatovumar
* Fix POS styling (#3713) @ishristov

### Improvements:

* Do not always provide counting in list views (#3696) @NicolasDorier
* Lightning: Catch and display external service error (#3710) @dennisreimann
* Add dark mode option for public pull payment and payment request views (#3707) @dennisreimann
* Show Shopify Order Id instead of Shopify order internal id (#3718) @Kukks

## 1.5.1

### Bug fixes:

* Do not show balance if can't get the balance (#3695) @NicolasDorier
* Fix performance issue on dashboard for big wallets (#3694) @NicolasDorier
* Do not crash if /apps/{appId} do not exists @NicolasDorier

### Improvements:

* Remove logs about pending invoices @NicolasDorier
* Add missing swagger doc for auto approval of payouts @Kukks

## 1.5.0

### New features:
* DASHBOARD!!! (#3530 #3629 #3631 #3654) @NicolasDorier @dennisreimann @dstrukt
* Payout Processors (#3476) @Kukks
* Allow pull payments, refunds to be automatically approved (#1851  #3682) @Kukks
* Greenfield: Add transaction info for on chain wallet (#3561) @bolatovumar
* Greenfield: Add label filter for on chain wallet transaction (#3588) @bolatovumar
* Greenfield: Add lightning payment info (#3557) @dennisreimann
* Greenfield: Add description hash to CreateLightningInvoiceRequest (#3559) @dennisreimann
* Allow Users to be disabled/enabled (#3639) @Kukks
### Bug fixes:
* Various UI fixes (#3599 #3577 #3624 #3642) @leesalminen @bolatovumar @dafunction @dennisreimann
* Fix bug when bumping fee (#3608) @bolatovumar
* Fix bug around Pay button html generator (#3646) @dennisreimann
* Fix Lightning addresses not being deleted after removing store (#3638) @Kukks
* Fix payment request redirect url (#3672 #3673) @dennisreimann
### Improvements
* Add additional rate providers as supported from CoinGecko @NicolasDorier
* Allow default payment method for Pay button (#3606) @bolatovumar
* Link directly to services from Lightning wallet page (#3593) @dennisreimann
* Use the store's default currency when creating entities (#3585) @dennisreimann
* Specify PayJoin enabled in Payment Link heading (#3614) @orangesurf
* LNURL: Use Lightning description template in LNURL metadata (#3667) @dennisreimann
* Design updates (#3647 #3653 #3565) @dennisreimann @dstrukt
* Hide empty plugins section (#3643) @dennisreimann
## 1.4.9

### Bug fixes:
* Fix plugin installer @Kukks
* Fix text around shopify settings @pavlenex

## 1.4.8

### New features:
* Greenfield: Send email via store (#3181) @woutersamaey @Kukks
* Greenfield: Configure store email settings (#3554) @Kukks
* Greenfield: Create lightning invoice with description hash (#3559) @dennisreimann

### Bug fixes:
* Fix crash on Wallet send page @bolatovumar
* Various UI fixes (#3519 #3522 #3543 #3553 #3584 #3578) @dennisreimann @bolatovumar @dafunction
* Fix plugin listing error due to Github rate limiting (#3502) @Kukks
* Fix shopify integration (#3589) @Kukks
* Fix order id in invoices updating (#3521) @woutersamaey
* Fix missing permissions in API keys creation pages @Kukks

### Improvements
* Various improvements around plugin system @dennisreimann @kukks
* Various tweaks around copy in UI @phershbe @dennisreimann @Kukks @pavlenex

## 1.4.7

### New features:

* Plugins: Expose file service @Kukks
* Plugins: Allow writing Greenfield API endpoints (#3495) @dennisreimann

### Bug fixes:
* Fix Shopify configuration UI due to shopify changes (#3479) @Kukks
* Various fixes related to store context (#3505) @dennisreimann @Kukks
* Various fixes related to plugin system @dennisreimann @Kukks
* Add missing documentation for wallet generation Greenfield API @Kukks
* Fix broken modal views on mobile (#3504) @dennisreimann
* Custom amount option in point of sale print view was not showing  (#3503) @Kukks
* Fix code copying buttons (#3489 #3499) @dennisreimann @bolatovumar
* Various UI fixes @dennisreimann @bolatovumar

## 1.4.6

### New features:

* Greenfield: Exposes LNUrl's comment and LN address in invoice's payment method (#3427) @Kukks
* Greenfield: Add maxFeePercent/maxFeeFlat to the lightning payment API (#3454) @dennisreimann @NicolasDorier
* Greenfield: Find 1 user by ID or by email, or list all users. (#3176) @woutersamaey

### Bug fixes:

* Fix: All PSBT flows were crashing @NicolasDorier

### Improvements:

* UI: Add border for mobile menu (#3477 #3469) @dennisreimann @dstrukt
* UI: Sticky headers improvements (#3471) @dennisreimann
* UX: Remove payment methods not currently configured when creating invoice (#3394) @bolatovumar
* UX: Add pull payment grouping options (#3177) @bolatovumar
* UI: Redesign Wallet UI (#3408) @dennisreimann @dstrukt
* UI: Remove storeid from the invoice's filter, as it is implicit @NicolasDorier

## 1.4.5

### New features:

* Ability to bump fees of transactions and invoices via CPFP (#3395) @NicolasDorier
* Add ability to add description to pull payment (#3363) @bolatovumar
* Greenfield: Can add store guest/owner to a store (#3425) @Kukks

### Bug fixes:

* Fix payment request archival actions (#3443) @dennisreimann
* Fix: filtering paid invoices in the invoice list wasn't working anymore (#3434) @dennisreimann @NicolasDorier
* Fix: Clicking any per-store nav links from the pairing approval page fails with 404 (#3431 #3438) @dennisreimann

### Improvements:

* UI improvements for the crowdfund settings (#3437 #3422) @dstrukt @dennisreimann
* Redirect to transactions list after wallet creation (#3451) @dennisreimann
* Setup guide: Link wallet setup always to BTC (#3442)

## 1.4.4

### Bug fixes:

* Ensure compresed public key is used for SIN generation even if uncompressed key was provided (fix #3432) (#3433) @CherryDT
* After login, redirect user to the main page even if a root app configured (#3429) @NicolasDorier
* docker-entrypoint would crash if missing ssh pubkey, but not the private key @NicolasDorier
* Error messages when starting BTCPay Server where not shown (Fix #3404) @NicolasDorier

### Improvements:

* UI: Use sticky headers for pages with tab navigation. (#3416) @dennisreimann
* UI: Prevent initial scroll to section nav (#3411) @dennisreimann

## 1.4.3

### Bug fixes:

* Ensure the swagger doc is semantically correct (#3390) @bolatovumar
* Fix crashes with some plugins (#3401) @Kukks
* Fix crash of payment request list (#3392) @NicolasDorier
* Reference correct payment type definition for webhook events in Swagger docs @bolatovumar
* Fix pay button type comparison (#3403) @dennisreimann
* No JS error in crowdfund if canvas unavailable @NicolasDorier
* Bump z-index on header (#3393, #3377) @bolatovumar

### Improvements:

* Delete user preferences cookie on logout (#3379) @dennisreimann
* Pay Button Alert: Add missing alert-link classes (#3397)
* Fix pay button type comparison (#3403 #3403) @dennisreimann
* Various CSS UI adjustment @dstrukt

## 1.4.2

### Bug fixes:

* Newly created guest cannot login (#3373) @dennisreimann
* Guest users shouldn't see Payouts menu item @NicolasDorier
* Add plugins link in server nav @Kukks

## 1.4.1

### Bug fixes:

* Fix: Existing Lightning addresses were not loaded (#3367 #3368) @Kukks

## 1.4.0

BTCPay Server started in August 2017 and meanwhile has been evolving incrementally thanks to the feedback of the community.

It was finally time to cleanup the UI/UX and technical debt we accumulated over the years.

We enumerate here a lot of new features and bug fixes, but we do not enumerate the UI/UX changes, we dedicated a separate [blog post for this topic](https://blog.btcpayserver.org/btcpay-server-1-4-0/).

The heavy lifting of this work has been mainly brought to you thanks to the collaboration of @dstrukt and @dennisreimann.
We thank also all the testers we brought us feedback, and all of you who participated in the weekly design meetings.

The work on the UI/UX is however never over and we will keep on improving it based on your feedback.

Note: If you are using our docker deployment on a raspberry pi 4, there is a small chance your docker version does not support the new docker image.
If you have any issue with raspberry pi 4, you need to update your docker version following steps on [this blog post](https://blog.samcater.com/fix-workaround-rpi4-docker-libseccomp2-docker-20/).
Note that you do not need to update libseccomp2, our update process does this for you automatically.

### New features:

* Greenfield: Add a `missingPermission` field to 403 errors (#3195) @NicolasDorier @woutersamaey
* Support for new TLS version of SMTP server (#3202) @NicolasDorier
* Greenfield: Added field "StoreId" to a Payment Request (#3223) @woutersamaey
* Greenfield: Can create a payment request without specifying currency (would then use store's default currency) (#3222) @NicolasDorier
* Add login code, for easy login to BTCPay Server via a mobile device (#2504) @Kukks
* Add LNUrl Auth support as second factor auth (#3083) @Kukks
* Batch unarchive invoices (#3264) @dennisreimann

### Bug fixes:

* Fix: BTCPay would crash if running in an unexpected working directory (#1894 #3295) @NicolasDorier
* Fix: Can't add security device on safari (#3197 #3322) @Kukks
* If a root path was used, the Notification dropdown wouldn't automatically fetch new notifications @NicolasDorier
* Clipboard wasn't working over http, mainly used in at home setups (#3296) @dennisreimann
* Greenfield: Creating a payment request would fail if expiry was specified (#3222) @NicolasDorier
* In wallet's receive if you copy a p2sh address, it would be truncated (#3218) @dennisreimann
* Shopify: Fix partial payments. Generate an invoice based on outstanding amount instead of total. (#3193 #3203) @Kukks
* BTCPay Server instance sends 2 emails after invoice is set as expired, paid or confirmed/complete (#968) @NicolasDorier
* Greenfield: Payment Method update was impossible if using internal ln node while being guest (#2860) @NicolasDorier
* Checkout: Error when changing payment method in invoice (#3075) @dennisreimann
* Greenfield: `created` field of payment request should be a unix timestamp @woutersamaey (#3221)
* Fix LN Node availability check (#3189) @dennisreimann
* Fix CSP violations in payment button page (#3334) @dennisreimann

### Improvements:

* Use the invoice terminology Processing/Settled in the UI rather than Paid/Confirmed/Complete.
* Add loading indicator for "Pay" button in POS terminal app (#3342 #3336) @bolatovumar
* Do not use uppercase in urls (#921) @NicolasDorier
* Add a copy Tor URL in the footer (#2692 #3290) @dennisreimann
* Improve permissions error messages of Greenfield API (#3256 #3212 #3220 #3204 #2795) @NicolasDorier @Kukks
* Greenfield API: Remove redundant/unused parameters in payment methods @bolatovumar
* Greenfield: Getting the fee rate should work with CanViewStoreSettings permission (#3217) @woutersamaey
* Add suggestion list for currency text inputs (#3347) @NicolasDorier
* Add warning about the security tradeoff the paybutton (#3340) @NicolasDorier
* Migrating from .NET 3.1 to .NET 6.0 @NicolasDorier
* Use C# 10.0 @NicolasDorier

### Breaking changes:

* If you activated plugins, you will need to update them.
* We removed support for ETH/ERC20
* Greenfield: `created` field of payment request should be a unix timestamp @woutersamaey (#3221)
* Some Rapsberry PI 4 deployment with old version of docker might experience issues (see [this blog post](https://blog.samcater.com/fix-workaround-rpi4-docker-libseccomp2-docker-20/) to update docker, libseccomp2 is already updated as part of our update flow)

## 1.3.7

### Improvements:

* Update of Bitbank rate provider (#3157) @junderw

### Bug fixes:

* Fix visual bug when decoding PSBT (#3172) @dennisreimann
* Swagger fixes: improve API docs and property types (#3170) @woutersamaey
* Fix copy pay button code (#3175) @dennisreimann
* Fix LN Node availability check (#3189) @dennisreimann
* `available` property of nodes returned by /api/v1/server/info wasn't actually set (ee1a034c0ab7744a2988e5da874084bc7dfa8b73) @NicolasDorier
* Format perk value correctly in crowdfund app (#3141) @bolatovumar
* Invoice page: Dropdown magically disappears (#3167 #3169) @trigger67

## 1.3.6

### Improvements:

* Fix breaking changes of LND API 0.14 @NicolasDorier

## 1.3.5

### Bug fixes:

* Fix: Checkout page of for invoices of 0 amount shouldn't crash, but 404 @NicolasDorier
* Swagger doc: Fix type of property cryptoCode (#3088) @ndeet
* Fix bug with fraction amount display in crowdfund app (#3098) @bolatovumar
* Swagger doc: Update Swagger docs for webhook event types (#3104) @bolatovumar
* Payout/pull payment page would crash if no payment method are set on the store @satwo

### Improvements:

* Add crypto code for invoice and pull payment payout API response (#3099) @bolatovumar
* Prevent creation of on-chain invoices below the dust limit (#3082) @satwo


## 1.3.4

### Bug fixes:

* Fix: Do not crash when redirect url is not provided to Authorize page @Kukks
* Fix: Disabling lightning should also disable LNURL @Kukks
* Fix: Paging in payouts did not take additional parameters in consideration @Kukks
* Fix: Payout actions button was misaligned @Kukks
* Fix: Amount validation for payout creation min amount was missing @Kukks

### Improvements:

* Point of Sale Print view improvements (#3050) @satwo @dennisreimann
* Upgrade to Bootstrap 5.1.3 @dennisreimann
* Updates display names (#3036) @dstrukt

## 1.3.3

### Bug fixes:

* LNAddress wasn't working if the store supported an altcoin @NicolasDorier
* Fix maintainance view @dennisreimann

## 1.3.2

This feature include a critical security patch. The vulnerability impacts owner of shared instances which share their internal lightning nodes. Credits to @yilakb to have noticed us.

### New Features:

* Greenfield: Adds the Archive status to Invoice model @TheHazeEffect
* Greenfield: Add pagination to the get invoices operation @TheHazeEffect

### Bug fixes:

* Crowdfunding topup invoice doesn't work when there isn't a perk added (#3048 #3064) @satwo
* Crowdfund: Fix perk value display (#3060) @dennisreimann
* Lightning address payment would fail if millisatoshi is not 0 mod 1000 on LND (#3056) @NicolasDorier
* The Test Connection feature during lightning setup was hidding cause of failure @NicolasDorier
* Creating a new invoice in payment request with LNURL activated would crash @NicolasDorier
* Improve error reporting in (#3065) @NicolasDorier
* After loading the Update PoS Settings page and selecting an item to edit, it will always show the price type selected as Fixed regardless of what the actual price type is. (#3049) @fabu21
* Fixes label on Point of Sale page (#3037) @dstrukt

### Improvements:

* If no default payment method, the fallback should be in order of preference: BTC, then Lightning (via BOLT11)
* UI Improvement of the maintenance page @dstrukt
* In the invoice's details page, show the url of webhook's deliveries (#3034) @satwo
* Improves upload button for files  (#3044) @dstrukt

## 1.3.1

### Bug fixes:

* Fix: The checkout page would reload the page when changing payment method, causing annoying an annoying flickering @NicolasDorier
* Fix: When browsing to BTCPay with explicit paymentMethodId such as `https://btcpay.../i/{invoiceId}/{paymentMethodId}`, it was impossible to switch to any other payment method @NicolasDorier

## 1.3.0

### Improvements:

* Various Bootstrap related updates (#2785 #2841 #2870 #2893 #2915 #2932 #2957) @dennisreimann @dstrukt @bolatovumar
* Various GreenField API improvements (#2817 #2880 #2905 #2934) @bolatovumar @kukks @woutersamaey
* PSBT UI improvements (#2713) @dennisreimann
* Revamp Theme system (#2794 #2927) @dennisreimann @dstrukt
* Revamp confirmation modals (#2614) @dennisreimann @dstrukt
* Unify Fido2 authentication under two-factor tab (#2866) @dennisreimann
* Remove slack link (#2887) @dstrukt
* Improve warning when creating invoice without wallet (#2844) @bolatovumar
* Improve public LN node info (#2876) @dennisreimann
* Adds social links to footer @1nF0rmed
* Switch to offcanvas navigation system @dennisreimann
* Crowdfund public UI re-design (#2918 #2926 #2938) @dennisreimann
* Remove Coinswitch entirely @kukks
* Improve display and structure of payment related configuration (#2945) @dennisreimann
* Coin selection improvements (#2956) @dennisreimann
* Add Passport hardware wallet option to the wallet import screens (#2962) @BitcoinQnA
* Improve language dropdown UX (#2972 #2976) @dennisreimann @satwo
* Add paging to pull payments list page (#2997) @kukks
* Pull payments & Payouts moved to store from wallet pages (#2987) @kukks
* Add number formatting in crowdfund app @bolatovumar
* Improve the language dropdown of the checkout (#2971) @satwo
* Validation of payment method criteria fails silently in keypad-only PoS (#2991) @satwo

### New features:

* Taproot support (#2830 #2837) @sageprogrammer @nicolasdorier
* Specify default payment method through UI and Greenfield API (#2815 #2986) @bolatovumar @NicolasDorier
* Disallow cancelling payment request when "Allow payee to create invoices in their own denomination" is not enabled (#2843) @bolatovumar
* Support custom currencies for Pay button generator (#2896) @bolatovumar
* Show total balance on wallets list (#2882) @maxdignan @dennisreimann
* Greenfield: Payment Settled Webhook event (#2944) @kukks
* Add ability to set invoice status from details page (#2923) @bolatovumar
* Add ability to accept tips in POS terminal (#2983) @bolatovumar
* Add ability to wipe all the transactions of a wallet for admins (#2857) @NicolasDorier
* Allow User to delete own account (#2949)  @kukks
* Allow email notifications when creating invoices from Web UI (#2959) @sipsorcery
* Dev Cheat mode (#2672 #2965) @NicolasDorier @woutersamaey
* Add support for CryptoMarket exchange rates (more accurate rates for Chilean Pesos, Brazilian Reals and Argentine Peso) @bolatovumar
* Add support for rpio exchange rate (close #2960) @NicolasDorier
* Greenfield: Provide negative undue when overpaid. (#2936) @kukks
* Support topup invoices in apps (#2958) @kukks
* Support Lightning in Pull Payments. (#2958) @kukks
* Support LNURL and Lightning address in Pull Payments (#2958) @kukks
* Add boolean overPaid to the invoice settled webhook @NicolasDorier
* Ability to display and update the appname in crowdfund and PoS @satwo
* Add ability to require refund email from app level @bolatovumar
* Azerbaijan support for the checkout (Orkhan Guliyev)

### Bug fixes:

* Fix Summernote editor (#2829) @dennisreimann
* Fix topup invoices not created when payment method criteria specified (#2847) @bolatovumar
* Check for empty theme URI before saving theme settings (#2851) @bolatovumar
* Signing a transaction with too many inputs (around 500), should not timeout @nicolasdorier
* Fix Vault issues: If signing took more than one minute, the connection to HWI would drop @nicolasdorier
* Fix CSP issues (#2872 #2946 #2954) @nicolasdorier @dennisreimann  @bolatovumar
* Fix issues with Authorization Request page (#2894) @bolatovumar
* Do not activate payment methods for non-new invoices @nicolasdorier
* Fix camera not working on wallet send (Fix #2922) @nicolasdorier
* Properly handle InvoiceMetadata string properties (Fix #2906) @NicolasDorier
* fix: Plugins disabled message never dissappers even after re-enabling it. @kukks
* Fix: Impossible to see relative time of transaction in wallet list @NicolasDorier
* Fix bug: Importing seed with Is hot wallet checked was not working (#2966) @NicolasDorier
* fix pos app logo (#2977) @satwo
* Fix cryptic error message issue (#2978) @Bananenbieger123
* Fix BIP21 pull payment support (#2985) @kukks
* Fix: favicon wasn't shown if using rootpath @NicolasDorier
* Fix: The redirect url of crowdfund invoices wasn't set correctly if rootpath is used @NicolasDorier
* Fix: Many SVG assets were not showing properly if rootpath is used @NicolasDorier
* Fix: Fonts and Home background not loading properly when using rootpath @NicolasDorier
* If the local culture of the server was not english, numeric values greenfield were not properly interpreted @NicolasDorier
* Default payment method settings was not working properly @satwo @NicolasDorier
 * Fix scanning of animated QR codes (#3003) @dennisreimann

## 1.2.4

Minor bug fixes release, update recommended for shared hosting. (#2851)

### Bug fixes

* If `Only enable the payment method after user explicitly chooses it` is enabled for a store and a payment method is unavailable, the server could become unresponsive. @NicolasDorier
* Authorize API key page was broken when trying to select specific stores (#2858) @bolatovumar
* The /docs page was broken in 1.2.3 due to CSP @NicolasDorier
* Fixing crashes happening when someone migrate from BTCPay Server altcoins edition back to bitcoin only @Kukks

## 1.2.3

This release fixes three XSS vulnerabilities. Those vulnerabilities only impacts shared BTCPay instances.
Special thanks to Ajmal "@b3ef" Aboobacker and Abdul "@b1nslashsh" muhaimin for finding them who contacted us through @huntrdev.
See [1](https://huntr.dev/bounties/ffabdac8-7280-4806-b70c-9b0d1aafbb6e/), [2](https://www.huntr.dev/bounties/32e30ecf-31fa-45f6-8552-47250ef0e613/) and [3](https://huntr.dev/bounties/0fcdee5f-1f07-47ce-b650-ea8b4a7d35d8/).

### Bug fixes:

* Use CSP to prevent future XSS vulnerabilities. (#2856, #2863) @NicolasDorier
* Fix XSS vulnerabilities in summernote, the rich text editor (#2859) @dennisreimann
* The page could crash if the user clicks too many time on Notificate 'Mark as Seen' @NicolasDorier
* Fix plugins page crashing @Kukks
* Fix page crash of the perk editor in the crowdfund settings when the title is not set @dennisreimann
* Do not generate payment methods when 0 amount invoice (#2776)
* When using the BTCPay Vault, some hardware wallet types were considered unknown @NicolasDorier

## 1.2.2

# Bug fixes:

* It was impossible to send from the wallet to more than two destinations (#2825) @NicolasDorier
* Fix rounding issue in the invoice refund flow (#2778, #2810) @NicolasDorier
* When cloning an expired payment request, the new payment request was also expired (#2820) @dennisreimann
* Fix instructions to import a coldcard wallet via file upload (#2809) @mandelbit
* Lightning payments should not be proposed for top-up invoices (#2772, #2780) @bolatovumar
* Typo fixes (#2774) @jorisvial
* Fix payjoin client to properly handle receiver using output substitution (#2677) @NicolasDorier
* The checkout would crash for some client if automatic detection of language was checked, and the browser was not setting the accepted language @NicolasDorier

## 1.2.1

### Bug fixes:

* Fix Display app on website root feature @NicolasDorier

## 1.2.0

### Improvements:
* Migrate to Bootstrap5 (#2490) @dennisreimann
* Greenfield: Server Info: Support all currency codes for sync status (#2511) @kukks
* Greenfield: Add StoreId to Invoice model (#2592)  @kukks
* Greenfield: Change `enabledOnly` filter to `enabled` @kukks
* Self host PoS app default images (#2449) @dennisreimann
* Various UI Tweaks and improvements (#2558 #2562 #2568 #2572 #2606 #2608 #2615 #2627 #2628 #2649 #2645 #2673 #2646 #2647 #2745 #2746) @dstrukt @dennisreimann @woutersamaey @johanf85 @bolatovumar
* Notify users to use newer BTCPay Vault app if necessary @nicolasdorier
* Set lightning invoice fallback in QR code as uppercase (#2492) @bjarnemagnussen @Kukks
* Optimize payout database fetching @nicolasdorier
* Wallet Signing UI improvements (#2559) @dennisreimann
* Add payjoin to hot wallet setup and turn on by default (#2450) @dennisreimann
* Add permission code to API page (#2599)  @woutersamaey @dennisreimann
* Introduce Server paging for Payouts List (#2564) @kukks @dennisreimann
* Hide referer URL to hide our BTCPay Server URL (#2655) @woutersamaey
* Deeper accessibility for plugin system @kukks
* Add webhook delivery status indicator (#2679) @bolatovumar
* Auto-select store when creating a new invoice (#2680) @bolatovumar
* Save paymentRequestId in Metadata when creating invoice for Payment Request (#2644) @woutersamaey
* Support multiple file upload (#2705) @cypherbeerus
* Improve Dutch translation (https://github.com/btcpayserver/btcpayserver/commit/7ac83575d4c50e42f2ecc02c8bf80f66697b6d57)  @woutersamaey
* Improve Portuguese translation (https://github.com/btcpayserver/btcpayserver/commit/7ac83575d4c50e42f2ecc02c8bf80f66697b6d57)  rafaelpac
* Improve payment view (#2748) @dennisreimann @dstrukt
* Improve Wallet Send UI (#2750) @dennisreimann
* Show new store warning icon only if neither on-chain wallet nor LN is configured (#2760) @bolatovumar
* Update successful refund message (#2764) @cypherbeerus
* Fix translation on finnish, bulgarian, Kazath (fa91174b1a310e46a37e1862f2b9c263f5e26408, 10e3595a829052573a9918eacafabc6d10e03ea6 965beebc6624906a1f3127623576088dee23e9bf) @NicolasDorier

### New features:
* Greenfield: Delete User API (#2340) @bolatovumar @kukks
* Can create invoices without a specific amount: Top-up invoices (#2730 #2659) @NicolasDorier
* Greenfield: Add misc/permissions to document the hierarchical structure (#2654) @nicolasdorier
* Greenfield: Add "skip" and "limit" params for onchain txs API endpoint (#2688) @bolatovumar
* Greenfield: Add `CanModifyInvoices` permission (#2595) @kukks
* Greenfield: Add text search terms to an invoice (#2648) @NicolasDorier
* Greenfield: Add Get store Payment methods API (#2545) @kukks @bolatovumar
* GreenField: Add Generate Store OnChain Wallet API (#2708) @kukks
* Test Webhooks functionality (#2474) @bolatovumar
* Allow marking payout as paid manually (#2539) @Kukks
* Pull payments: Detect External OnChain Payouts (#2462) @Kukks
* Auto-detect language on payment page (#2552) @woutersamaey @Kukks
* Support spending to Taproot (#2718) @nicolasdorier
* Show Immature Balance in walletsend page (#2731 @732) @sageprogrammer @nicolasdorier
* Add hebrew translation for checkout (https://github.com/btcpayserver/btcpayserver/commit/7ac83575d4c50e42f2ecc02c8bf80f66697b6d57) @jonathanalevi
* Add korean translation for checkout (https://github.com/btcpayserver/btcpayserver/commit/7ac83575d4c50e42f2ecc02c8bf80f66697b6d57)  Saeyoung Kim

### Bug fixes:
* Fix issue with mysql migration and maxLength (#2541) @jkljajic
* Fix broken shopify links @kukks
* Fix bug with LN payment method API endpoint throwing 500 (#2567) @bolatovumar
* Fix various wording and typos @pavlenex @britttttk @Zaxounette Jimi Ford
* Fix visual bug with invoices search help text overlapping invoice action buttons (#2583) @bolatovumar
* Fix: Invoice Search Text crashes invoice creation when value is too long (#2675) @kukks
* Greenfield documentation fixes (#2657 #2674 #2681 #2598) @woutersamaey @bolatovumar
* Re-enable "Create" button for invoices on correct form input (#2694) @bolatovumar
* Fix: Payment Request status does not update on invoice marked events or when pr amount is changed (#2700) @kukks
* Properly clip taxIncluded and invoice's amount (#2724) @nicolasdorier
* Fix PoS bug on dark mode (#2743) @dennisreimann
* Remove support for payout to a Bitcoin Url (#2766) @NicolasDorier
* Fix: Support Clightning 0.10.1 @kukks

## 1.1.2

* Fix: Unable to activate shopify integration @Kukks

## 1.1.1

### Improvements:

* Update BC-UR bundle and support decoding hex format of wallet (#2505 #2499) @Kukks

### Bug fixes:

* During refund or payout, some payments issued from BTCPay were not properly detected. (#2513 #2518) @Kukks @NicolasDorier
* Fix payment button steps and validation range (#2506 #2503) @Kukks
* The local culture of the server could break some feature on BTCPay Server (#2512) @NicolasDorier
* Make sure unaccounted payments (double spent payments, or payjoin original transaction), are not accounted by the payment requests and crowdfund app @NicolasDorier
* Coinswitch page was not reflecting correctly in the side navigation @kukks
* Coinswitch showed as enabled when it was configured but disabled @kukks
* Lightning payment were not detected if `Only enable the payment method after user explicitly chooses it` was checked for the store @kukks

## 1.1.0

### Improvements:

* Improving navigation between files and storage services and rewording info text (#2272) @rockstardev
* UI: Header and navigation improvements (#2412 #2378) @dennisreimann @dstrukt
* Plugins will be disabled in the case of an unrecoverable runtime error caused by a plugin @Kukks
* UI: Improve Lightning setup page (#2348 #2477) @dennisreimann @dstrukt
* Greenfield: Provides unconf/conf balance, keypath + address + timestamp + confirmation count of utxos @Kukks
* Add `BTCPAY_TOR_SERVICES` configuration to expose tor services via the server settings. Useful for integration with self-hosted node such as Umbrel (#2388) @Kukks @junderw
* Payment methods can be toggled directly from the update store page, rather than inside the page of each payment method (#2469) @dennisreimann
* Start separation of Coinswitch feature and Shopify integration as plugins (#2384 #2390) @Kukks
* Greenfield: Ability to pass more query parameters to filter results of api/v1/invoices @SakerOmera
* Human friendly error if webhook or webhook delivery not found @NicolasDorier
* Add button to copy API key to clipboard (#2439) @bolatovumar

### New features:

* Support WebAuthN/FIDO2 as second factor @Kukks
* Can get a receive address in the wallet accepting Payjoin (without creating an invoice) @Kukks
* Can disable modification of SSH settings via the server settings to prevent escalation of privilege. (See #2468) @NicolasDorier
* Manual coin selection has a "confirmed utxo" filter @Kukks
* Greenfield: Can query fee rate @Kukks
* New setting for checkout: Ability to activate specific payment methods after the creation of the invoice @xpayserver @Kukks @rockstardev

### Bug fixes:

* Fix: Clicking on "Unreserve this address" was not properly reflected in the UI @Kukks
* Fix: Block explorer links for signet @kristapsk
* Fix: Typo in PoS cart view (#2428) @MaxHillebrand
* Allow accessing "misc/lang" endpoint with Greenfield auth schemes (#2471) @bolatovumar
* Greenfield: Fix typo of webhook type OrignalDeliveryId => OriginalDeliveryId @NicolasDorier
* If the posData property of invoice metadata was not a JObject, the invoice would crash @Kukks
* If a store was created via the Greenfield API, warning signs of unconfigured stores would not appear. (Fix #2434) @bolatovumar
* Do not crash if plugin folder mismatches plugin identifier @Kukks
* Fix notification count on mobile (#2483) @dennisreimann
* Fix: Passing invalid query parameters or route value in the Greenfield API should returns HTTP 422 + validation details rather than empty 400. @NicolasDorier
* Greenfield: Deleting a store in the server, should delete only webhooks of this store @NicolasDorier

### Miscellaneous

* Add user id in logs when somebody logs in. @NicolasDorier
* Fix: Json type in doc API @g33kme

## 1.0.7.2

Small release fixing bugs introduced in 1.0.7.1:

### Bug fixes:

* The date in invoice page were not showing anymore the browser date time, but the server date time. (@NicolasDorier)
* Apps on root where not working anymore, redirecting to login page rather than showing the app (see #2414) (@bolatovumar)

## 1.0.7.1

### Improvements:

* Add user email search/sort @bolatovumar
* Fix pay button link preview (see #2396) @bumbummen99
* Change display date format on view pull payments (see #2339) @AlexGidge
* Update form required input styling (see #2373) @bolatovumar
* Order file uploaded list by descending timestamp (#2273) @bolatovumar
* Remove misleading title from hint icon @dennisreimann
* Make dates/timespan swagger docs more clear (#2399) @Kukks
* Add rate limiter for forgotpassword @NicolasDorier
* Upgrade Boostrap to v4.6 and jquery to 3.6.0 @dennisreimann
* Use better PRNG for payjoin input selection @NicolasDorier
* Decrease authentication cookie timeout after password change from 30min to 5min @NicolasDorier
* Use secure/http-only cookies for preferences @NicolasDorier

### Bug fixes:

* Ensure submitting empty currency does not break update PoS page (#2376) @bolatovumar
* Fix point of sale item newline break (#2366) @Kukks
* Validate filename in file upload endpoints @NicolasDorier
* Turn off autocomplete for BIP39 Seed or HD private key inputs @nosovk
* Fix payment request template body/page height and footer style @Patrick

## 1.0.7.0

### Features:

* New Wallet Setup UI (see #2164, #2296) @dennisreimann @dstrukt
* Greenfield: New on-chain wallet API @Kukks
* Greenfield: Ability to configure store's lightning payment methods @Kukks
* Allow an invoice to be marked invalid/complete even from the `new` state @Kukks
* Point of Sale and Crowdfund: Allow custom buy button text (see #2299) @dennisreimann
* Specter wallet file import (see #2252) @dennisreimann

### Improvements:

* Reenabling uppercase BECH32 in QR codes (see #2110) @rockstardev
* If a store is set to internal node, use "Internal Node" as connection string rather than the actual connection string. @NicolasDorier
* Improve Policies options UX in server settings (see #2307) @dstrukt @dennisreimann
* Fix view payment request loading spinner alignment @bumbummen99
* Fix cart pay button loading spinner vertical alignment @bumbummen99
* Invoices list: Remove icon indicator for onchain (see #2240) @dennisreimann
* Login: Improve tab navigation for input fields (see #2258) @dennisreimann

### Bug fixes:

* Hovering the mouse pointer on invoice logs row would make them unreadable @bolatovumar
* Remove exchange rates that lost support in Coingecko @NicolasDorier
* Get invoice in greenfield was crashing if invoiceId did not exist @NicolasDorier
* Getting a file from the storage service which did not exist would return http 500 instead of 404 @NicolasDorier
* Fix direct URL for local storage with custom root path #2318 @bolatovumar
* The pay button would not show up properly on some websites @bolatovumar
* Profile email change should check email's availability @NicolasDorier
* Fixed mysql/sqlite support @ketominer
* Checkout: Fix scan/copy tab sizes with varying content (see #2264) @dennisreimann
* Greenfield: Lightning API would return HTTP 500 if store owner did not set the connection string @dennisreimann
* Point of Sale: The custom price was not properly working (see #2248) @bolatovumar

## 1.0.6.8

This release is trying some improvement to decrease the chances of being falsy flagged by Google Safe Browsing.

* Remove Tor URL from login page (useless now thanks to the url bar link) @dennisreimann
* Remove allowtransparency from checkout overlay @dennisreimann
* Remove clipboard code from the login page (was used to copy the tor url) @dennisreimann
* Rename some pages from PascalCase to lowercase. (Register => register, Login => login) @dennisreimann

## 1.0.6.7

### Bug fixes:

* Reverted the new Greenfield API: Can configure lightning payment methods @NicolasDorier

## 1.0.6.6

### Bug fixes:

* Load correct connection string when using SQLite @Kukks
* Greenfeld API: Invoice Metadata update was not updating @saliehendricks
* Prevent access to wallet pags if wallet not set @dennisreimann

### New features

* Greenfield API: Can configure lightning payment methods @Kukks

## 1.0.6.5:

### Improvements:

* Support a subset of output descriptor in the wallet setup @Kukks
* Improved styling of the notification dropdown (see #2167) @bolatovumar @dennisreimann
* API keys and server's url can be shown as QR Code to facilitate pairing @Kukks
* Greenfield API: Add `DefaultPaymentMethod` to the store's settings @Kukks
* Greenfield API: Can configure on-chain payment methods @Kukks @NicolasDorier
* UI Improvements (see this [commit list](https://github.com/btcpayserver/btcpayserver/pull/2151/commits)) @dennisreimann

### Bug fixes:

* Always normalize the invoice's currency in uppercase @NicolasDorier
* If a label on a wallet's transaction does not have color, it should still show it @NicolasDorier
* Do not include Tor Location header when querying the modal checkout (see #2180) @Kukks
* Webhooks should not be randomly deleted anymore. @NicolasDorier
* Fix header not showing properly after login to BTCPay Server (see #2155) @dennisreimann
* Bug: Searching invoices was timing out if there was too much invoices @rockstardev @Kukks

### Miscellaneous:

* Removing the old text search engine (DBreeze) @rockstardev @Kukks
* Add doc for asking permissions to BTCPayServer see [link](docs/greenfield-authorization.md). @Kukks

## 1.0.6.4:

### Bug fixes:

* Fix coin selection label filter (@Kukks)
* Greenfield: Fix behaviour after first admin signup (see #2150) @dennisreimann
* Greenfield: If invoice is not found, error 404 should be returned rather than a crash @dennisreimann
* Attempt to fix sometimes broken Mark as Seen button @NicolasDorier

### Improvements

* Improve the invoice details view (see #2144) @dennisreimann

### Miscellaneous

* API Keys: Add usage examples link to docs @dennisreimann

## 1.0.6.3:

### New features

* Greenfield API: Can update invoice metadata @Kukks
* Greenfield API: User notifications API @Kukks
* Greenfield API: Can specify the preferred language when creating an invoice @NicolasDorier
* Greenfield API: Can specify the URL to redirect customer to when he paid when creating an invoice @NicolasDorier
* Greenfield API: Expose the `checkoutLink` of the created invoice, where you need to redirect your customer to pay in BTC @NicolasDorier
* Add a `Mark all as seen` button to the notification dropdown @bolatovumar
* Support of Armenian language in checkout page. Thanks to Mash Mashok
* Add ability to add custom CSS to pull payments @bolatovumar
* Introduce some basic spam protection for hosts with open registration (see #2106) @Kukks


### Improvements

* Hide pagination & page size when not necessary (#2122) @dennisreimann @dstrukt
* Document in `/docs` miscellaneous features of BTCPay (such as optional arguments of the checkout page) @NicolasDorier
* UI: Improve mobile store setup view @dennisreimann @dstrukt
* Improve U2F support, should leverage OS support and now work properly on mobile Safari (See #2086) @dennisreimann
* Improve how new label picked their color (See #2072) @bolatovumar
* Improve the design of transaction's label dropdown to fix display issue when there are too many (see #2078) @bolatovumar

### Bug fixes:

* Ensure campaign status is shown correctly in minimal crowdfund app (#2062) @bolatovumar
* Can remove automatic labels from invoices @NicolasDorier
* Fix Pay Button Link preview when app mode chosen (#2115) @Kukks
* If the user was not admin, the API Keys page was ignoring some of the checked permissions @NicolasDorier
* Greenfield API: If invoice creation failed for some reason, BTCPay would give a HTTP 500 error without details helping the user. @NicolasDorier
* Fix performance issue: Some invoice queries were causing a full table scan on all invoices rather than using an index. @NicolasDorier
* Fix: Importing an incorrect wallet from BlueWallet was crashing (#2098) @Kukks
* Fix classic theme for payment requests (Fix #2084) @dennisreimann

### Miscellaneous:

* Removing the bitpay invoice translator @NicolasDorier
* Improve the documentation of API Keys page @NicolasDorier
* Fix doc for create invoice request's metadata @NicolasDorier
* Fix docs for pull payments @Kukks

## 1.0.6.2:

*  Reverting uppercasing of Bech32 addresses in QR code (@Rockstardev)

It turns out this is not compatible with every wallets.

## 1.0.6.1:

### Bug fixes:

* The sync popup when the nodes are synching was not showing up (@Kukks)

## 1.0.6.0:

### Important security fix

* Due to a privacy leak vulnerability, users of the payment button are strongly encouraged to update as soon as possible.

### New features

* Add QR code scan/show for PSBT  + Import wallet via QR [spec](https://github.com/BlockchainCommons/Research/blob/master/papers/bcr-2020-005-ur.md) (supported by Cobo vault / Blue wallet) (#1931)
* Checkout experience: Unified QR Code for on-chain and offchain payment (ref #2060) (@rockstardev)
* Greenfield: Clean webhook API github-like (ref #2058) (@NicolasDorier @Kukks)
* Greenfield: Can query invoice payment data (@Kukks)
* Allow users to select block explorers from a list or specify their own URL  (@Kukks)
* Allow disabling live notifications globally and disabling specific notifications per user (ref #1991) (@Kukks)
* Allow custom redirect_url for PoS (ref #1924) (@mariodian)
* A new experimental plugin system (@Kukks)

### Improvements

* UI: Make store setup more intuitive (ref #2011) (@dennisreimann @dstrukt)
* UI: Improve payment request design (ref #2011) (@dennisreimann @dstrukt) (ref #2011) (@dennisreimann @dstrukt)
* UI: Improve pull payments design (ref #2011) (@dennisreimann @dstrukt)
* UI: Improvement of the modal checkout overlay  (see [this comment](https://github.com/btcpayserver/btcpayserver/pull/1930#issuecomment-701298441)) (@dennisreimann)
* BTCPay Server vault operations can now be retried without having to refresh the page (@NicolasDorier)
* UX: Warning and hint system for stores not completely set up (@dennisreimann @rockstardev)
* Greenfield (Breaking change): Invoice state renamed `Confirmed/Complete` to `Settled`. (@NicolasDorier)
* Greenfield (Breaking change): Invoice state renamed `Paid` to `Processing`. (@NicolasDorier)
* Breaking change: Remove SQLite as the default database option (@Kukks)
* UI: Make sure transaction labels display correctly when there are many (ref #2076) (@bolatovumar)
* UI: Properly center payment button content (@bolatovumar)
* UI: Improvement of the lightning node info view (ref #2066) (@dennisreimann)
* Share the link of a pay button so one can embed in a QR code (fix #635) (@Kukks)
* Checkout experience: Make QR codes with bech32 uppercase again (@rockstardev)
* Add warning if the merchant setup invoice confirmation to zero conf (@bolatovumar)
* Adds a warning to configure the e-mail server before "Requires a confirmation mail for registering" checkbox can be checked if e-mail server is not configured. (@bolatovumar)
* Payment requests: Partially paid invoices are reused for future payments in payment requests. (@NicolasDorier)
* API Keys UI: Properly align form items (@dennisreimann)
* Wallets: By default, created PSBT were including previous transactions. Some hardware wallets ended up returning timeouts, so we reverted this decision. (@NicolasDorier)

### Bug fixes:

* Fix payment button page title (ref #1952) (@sgracia13)
* Do not log the database connection string (@Kukks)
* Payjoin: Use base64 instead of hex for BIP78 (fix #1984) (@Kukks)
* If a password fail to be reset by mail, show proper error (fix #1986) (@NicolasDorier)
* Email was not included in the invoice text search (@Kukks)
* Greenfield: The create invoice route should not sending back generic errors if it fails (@dennisreimann)
* Fix-up links which were ignoring custom root path (@bolatovumar)
* Greenfield: Opening a channel with lightning was not working properly (ref #2054) (@dennisreimann)
* Docs: Create invoice route was referencing the wrong type in the doc (@dennisreimann)
* Payment Request user input rounding issue (ref #2014) (@Kukks)
* In store settings, the create new token button was returning an error (@NicolasDorier)
* Wallet: When clicking on the app's label of a transaction, an error 404 occured (@Kukks)
* Checkout experience: If coinswitch was activated, the altcoin tab was missing (@Kukks)
* If Email verification is turned off but you requested a forgot password form, it would ignore the request internally. (@Kukks)
* Docs: Fix swagger format for dates (@Kukks)
* Payjoin: Do not include maxadditionalfeecontribution if there is no change. (ref #2007) (@NicolasDorier)
* Checkout: If an invoice accepting lightning payments was partially paid, the payment of the new lightning invoice was buggy. (@Kukks)

## 1.0.5.9:

### Bug fixes:

* If there was too many pending invoice, postgres would be overwhelmed and freeze some requests (Igor Rylko)
* Emails were not included in the text search anymore @Kukks
* Payjoin: Do not include maxadditionalfeecontribution if there is no change. @NicolasDorier

## 1.0.5.8:

### Bug fixes:

* Fix payjoin client implementation (was sending hex instead of base64) @Kukks
* Fix: The send wallet, by default, should not include the previous transaction (timeouts issue with some hardware wallets) @NicolasDorier
* Do not log the database connection string @Kukks
* If a password fail to be reset by mail, show proper error @NicolasDorier
* When you map a specific domain to an app, when there's no app created there's a blank page @Kukks

### Bug fixes:

* Mark Shopify orders paid on invoice payment confirmed @rockstardev
* Fix: notification of new BTCPay Server not showing properly @rockstardev
* Fix: When collapsed, the sync window blocks the version text in the footer #1941 @Kukks
* Fix: Not possible to delete a user if U2F is enabled. @Kukks
* Fix onion location not always working #1947 @Kukks
* Fix invoice exception status not displaying in list #1963 @Kukks
* Fix: Is administrator checkbox does not work on create user page @NicolasDorier

## 1.0.5.7:

### Bug fixes:

* Mark Shopify orders paid on invoice payment confirmed @rockstardev
* Fix: notification of new BTCPay Server not showing properly @rockstardev
* Fix: When collapsed, the sync window blocks the version text in the footer #1941 @Kukks
* Fix: Not possible to delete a user if U2F is enabled. @Kukks
* Fix onion location not always working #1947 @Kukks
* Fix invoice exception status not displaying in list #1963 @Kukks
* Fix: Is administrator checkbox does not work on create user page @NicolasDorier

### Improvements:

* Add specter desktop to the list of Bitcoin RPC compatible wallet @NicolasDorier
* If some operation using BTCPay Server Vault fails, add a retry button so the user does not have to refresh the page. #1937 @NicolasDorier
* Do not show password in clear text in email configuration (Fix #1790) @NicolasDorier
* Showing CheckForNewVersions checkbox only if BTCPAY_UPDATEURL is set @rockstardev
* Add Created date to user, add verified column in list and make user list use same model as modern lists @Kukks
* Do not overlap the footer. Also removed the underline for the toggle button in chrome. Fixes #1946 @dennisreimann
* Improve notifications dropdown #1961 @dennisreimann

### Altcoins

* Fix: DOGE could be trapped, due to our sanity check of absurdly high fee of "1 DOGE". @NicolasDorier

## 1.0.5.6:

### New features:

* Shopify support @rockstardev @Kukks
* Can enable/disable any payment method based on the amount of the invoice #1871 @xpayserver
* New Invoice API in Greenfield (Still incomplete, more for next release) @Kukks @NicolasDorier
* A new light view more adapted for Point of Sale @mariodian
* Allows administrator to invite new users via link and email @Kukks
* New labels in the wallet for payment requests, apps, and improvement of the payout label @Kukks
* Allows entry in wallet send via fiat #1891 @Kukks
* Allows partial invoice refund #1882 @Kukks
* In the Request API key flow, let the user be redirected to the original website #1800 @Kukks @dennisreimann ([more info](https://docs.btcpayserver.org/API/Greenfield/v1/#tag/Authorization))
* Invoice logs now show their severity #1681 @Kukks (see https://i.imgur.com/eyMO9M3.png)
* Add store sort (#1861) @bolatovumar

### Improvements:

* Update PSBT and PSBT sent to Hardware wallet will include `non_witness_utxo` by default, when possible, to match Bitcoin Core 0.20.1 behavior. @NicolasDorier
* Adjust invoice badge styling (#1906) @bolatovumar
* Invoice notification email improvements (#1875) @dennisreimann
* Improvement of the UX flow for requesting an API Key of a BTCPay Server user (#1898) @dennisreimann
* Don't send notification email for expired invoices @dennisreimann
* Greenfield API: Add `Roles` property to the user data. @dennisreimann
* Remove Changelly integration @Kukks
* Better wording in transaction list page #1887 @maltokyo
* Fix alerts text break. #1865 @bolatovumar
* Remove Tor link from navbar @NicolasDorier
* Improve invoices list view #1815 @dennisreimann
* Improve sync progress dialog #1929 @Kukks
* Show index of payment address for onchain payments @Kukks

### Bug fixes:
* When an invoice is partially paid on-chain and allow off-chain, a new lightning network invoice should be created for the remainder of the payment. @Kukks
* Changing the inventory of a PoS item was not working properly (@mariodian)
* Greenfield API: The internal lightning API was returning error 403, even when used as an administrator (@Kukks)
* Using lightning charge as lightning network invoice provider over HTTP was not working properly @bolatovumar
* Fix: If the hot wallet failed to sign a PSBT, should not show a blank page crash (@NicolasDorier)
* Fix bug: The wallet was sending only round amount of sat per byte (@NicolasDorier)
* "Attempt MySql Fix" #1856 @Kukks
* Fix HitBTC rate provider again @NicolasDorier

### Altcoin build
* MonetaryUnit support (MUE) @sotblad
* ETH/ERC20 support @xpayserver
* Fix monero crash #1883 @Kukks

## 1.0.5.5:

### Improvements
* UI: Improve mobile login views (#1833 @DennisReimann)
* Pull payments claim & payout notification rewording (#1825 @Zaxounette)
* Do not load webfonts from google font server, serve locally (@DennisReimann)

### Bug fixes
* Fix some crashes when switching from Altcoins edition to Bitcoin-Only. (@Kukks @NicolasDorier)
* Fix invoices CSV Export formatting introduced in 1.0.5.4 (@NicolasDorier)
* UI: Fix custom-select glitch (#1822 @DennisReimann)
* Fix crash with hitbtc rate provider (@NicolasDorier)

## 1.0.5.4:

### New features and improvements
* BTCPayServer has now two different build Bitcoin-Only and Altcoins. See more [in our blog post](https://blog.btcpayserver.org/btcpay-server-1-0-5-4) (@xpayserver)
* Wallet UI improvement #1763 @dennisreimann
* Improve pull payment view #1764 @dennisreimann
* Login/Register view UI improvements #1752 @dennisreimann
* Manage store UI improvements #1761 @dennisreimann
* Improve the UX when creating a new seed #1745 @dennisreimann
* Allow selection of all notifications at once in notification list #1751 @bolatovumar
* Add filtering to Payment Requests @rockstardev
* Last filter used in payment requests and invoice list are now saved in user's preferences (cookie) #1775 #1498 @rockstardev
* Notification when new version of btcpayserver is available. $1420 @rockstardev
* Can sort apps list by store name, name or app type #1753 @bolatovumar
* Improve query performance when listing invoices @NicolasDorier
* Add margin to Delete store #1773 @bolatovumar
* Add pagination to wallet's transaction page #1772 @bolatovumar
* Improve VSCode user development experience #1769 @msafi
* Fix error message positioning in PoS #1759 @bolatovumar
* Fix swagger doc for approve payout @NicolasDorier
* Use BTCPay doc for RBF explanation tooltip @britttttk
* Allow mass archiving of invoices #1742 @bolatovumar
* Improve 2FA UI #1741 @dennisreimann
* .gitignore .DStore @Eskyee
* Allow RPC access in services when the node is synching @NicolasDorier

### Bug fixes
* Fix: In the PoS app, embedded CSS was ignored. @NicolasDorier
* Fix error when modifying user who does not have admin right. #1793 @NicolasDorier
* Fix null instance on invoice when using paymentCurrencies #1766 @Kukks
* Fix: Sluggish scrolling in pages having a rich text editor @dennisreimann
* Fix: Crash in payment request if there is several invoice in "new" state @Kukks
* Fix: Crowdfund app doesn't count old invoices. It was only invoices created after enabling the option. @Kukks

### Security fixes
Those are low risk injection vulnerabilities.
* Prevent script injection via X-Forwarded-For (reported by @benichmt1) @NicolasDorier
* Prevent script injection via the csv invoice export (reported by @benichmt1) @NicolasDorier

### Altcoins

* BTCPay Server build is Bitcoin Only by default. If you are developer and wants to work on the altcoins build, please read [the documentation](https://docs.btcpayserver.org/LocalDevelopment/).
* Show sync progress for monero and show amount of monero payment #1729 @xpayserver

## 1.0.5.3:

### Bug fixes
* Payouts list incorrectly filtered if more than two pull payments (@arc3x)
* Make it possible to refund invoice in the invalid state (@NicolasDorier)
* Sane error message from the server if Basic authentication is not properly encoded in base64 (@Kukks)
* Fix typos in pull payments (@Zaxounette)

### New features and improvements
* Add download PSBT button in the PSBT's screen of the wallet (@Kukks)
* Upload PSBT button now support both, a file with base64 PSBT in it, or the raw bytes (@Kukks)
* Make payjoin works with hardware wallets, need latest release of BTCPayServer Vault (@Kukks)
* Better design for 2FA config screens (@Kukks #1735)
* Enable CORS on greenfield API (@Kukks)
* UI cleanup in the account section (@dennisreimann see #1721)
* Improve information structure in the store's lightning page (@dennisreimann see #1706)
* Various code cleanup (@rockstardev)
* Set noindex, nofollow on the payment request page and pull payment page (@RiccardoMasutti)
* Improve "Send" screen address field UX (@bolatovumar #1723)
* Fix appearance of table in pull payments page (@bolatovumar #1732 and #1733)
* Improve service settings,  email settings, improve users list, U2F, 2FA, login view and maintenance page (@dennisreimann #1727)
* Update translation for Deutsch and አማርኛ (Peter Battermann and @lbtil)


## 1.0.5.2:

### Bug fixes
* Do not include the Onion-Location header for custom domains mapping (@NicolasDorier)
* Fix warning message when using SQLite (@NicolasDorier)
* Create store could be called with a scoped store's modify apikey (@NicolasDorier)
* Crowdfunding app used with a custom domain were showing blank page (@NicolasDorier)

## 1.0.5.1:

### Bug fixes
* Point of sales app used with a custom domain were showing blank page (@NicolasDorier)

## 1.0.5.0:

### New Features and improvements
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
* Ensure dropdown in checkout page does not overflow (@bolatovumar)
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
* Fix dark theme contrast for Payment Requests (@bolatovumar and @dennisreimann #1488)
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
* bolatovumar
* vswee
