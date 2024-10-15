using System;
using System.Collections.Generic;

namespace BTCPayServer.Services
{
    public partial class Translations
    {
        static Translations()
        {
            // Text generated by UpdateDefaultTranslations.
            // Please run it before release.
            var knownTranslations =
"""
{
  "... on every payment": "",
  "... only if the customer makes more than one payment for the invoice": "",
  "<span class=\"currency\">{0}</span> closing channels": "",
  "<span class=\"currency\">{0}</span> confirmed": "",
  "<span class=\"currency\">{0}</span> in channels": "",
  "<span class=\"currency\">{0}</span> local balance": "",
  "<span class=\"currency\">{0}</span> on-chain": "",
  "<span class=\"currency\">{0}</span> opening channels": "",
  "<span class=\"currency\">{0}</span> remote balance": "",
  "<span class=\"currency\">{0}</span> reserved": "",
  "<span class=\"currency\">{0}</span> unconfirmed": "",
  "A given currency pair match the most specific rule. If two rules are matching and are as specific, the first rule will be chosen.": "",
  "A self-hosted, open-source bitcoin payment processor.": "",
  "Access Tokens": "",
  "Account": "",
  "Account key": "",
  "Account key path": "",
  "Add additional fee (network fee) to invoice …": "",
  "Add Address": "",
  "Add Exchange Rate Spread": "",
  "Add hop hints for private channels to the Lightning invoice": "",
  "Add Role": "",
  "Add Service": "",
  "Add User": "",
  "Add Webhook": "",
  "Additional Actions": "",
  "Admin API access token": "",
  "Admin must approve new users": "",
  "Administrator": "",
  "Advanced rate rule scripting": "",
  "Allow anyone to create invoice": "",
  "Allow form for public use": "",
  "Allow payee to create invoices with custom amounts": "",
  "Allow payee to pass a comment": "",
  "Allow Stores use the Server's SMTP email settings as their default": "",
  "Always include non-witness UTXO if available": "",
  "Amazon S3": "",
  "Amount": "",
  "API Key": "",
  "API Keys": "",
  "App": "",
  "App Name": "",
  "App Type": "",
  "Application": "",
  "Apply the brand color to the store's backend as well": "",
  "Approve": "",
  "Archive pull payment": "",
  "Archive this store": "",
  "At Least One": "",
  "At Least Ten": "",
  "Authenticator code": "",
  "Auto-detect language on checkout": "",
  "Automatically approve claims": "",
  "Available Payment Methods": "",
  "Azure Blob Storage": "",
  "Backend's language": "",
  "Batch size": "",
  "BIP39 Seed (12/24 word mnemonic phrase) or HD private key (xprv...)": "",
  "blocks": "",
  "Brand Color": "",
  "Branding": "",
  "BTCPay Server currently supports:": "",
  "But now, what if you want to support <code>DOGE</code>? The problem with <code>DOGE</code> is that most exchange do not have any pair for it. But <code>bitpay</code> has a <code>DOGE_BTC</code> pair. <br />\n                        Luckily, the rule engine allow you to reference rules:": "",
  "Buyer Email": "",
  "Callback Notification URL": "",
  "Can use hot wallet": "",
  "Can use RPC import": "",
  "Celebrate payment with confetti": "",
  "Check releases on GitHub and notify when new BTCPay Server version is available": "",
  "Checkout Appearance": "",
  "Choose your import method": "",
  "Choose your wallet option": "",
  "Clone": "",
  "Coingecko integration": "",
  "Colors to rotate between with animation when a payment is made. One color per line.": "",
  "Confirm new password": "",
  "Confirm password": "",
  "Connect an existing wallet": "",
  "Connect hardware&nbsp;wallet": "",
  "Connect to a Lightning node": "",
  "Connection configuration for your custom Lightning node:": "",
  "Connection string": "",
  "Consider the invoice paid even if the paid amount is … % less than expected": "",
  "Consider the invoice settled when the payment transaction …": "",
  "Contact URL": "",
  "Contact Us": "",
  "Contribution Perks Template": "",
  "Count all invoices created on the store as part of the goal": "",
  "Create": "",
  "Create a new app": "",
  "Create a new wallet": "",
  "Create account": "",
  "Create Account": "",
  "Create Form": "",
  "Create Invoice": "",
  "Create Pull Payment": "",
  "Create Request": "",
  "Create Store": "",
  "Create Webhook": "",
  "Create your account": "",
  "Create your store": "",
  "Crowdfund": "",
  "Currency": "",
  "Current password": "",
  "Custom": "",
  "Custom CSS": "",
  "Custom HTML title to display on Checkout page": "",
  "Custom sound file for successful payment": "",
  "Custom Theme Extension Type": "",
  "Custom Theme File": "",
  "Dashboard": "",
  "Date": "",
  "days": "",
  "Default currency": "",
  "Default Currency Pairs": "",
  "Default language on checkout": "",
  "Default payment method on checkout": "",
  "Default role for users on a new store": "",
  "Delete store {0}": "",
  "Delete this store": "",
  "Derivation scheme": "",
  "Derivation scheme format": "",
  "Description": "",
  "Description template of the lightning invoice": "",
  "Destination Address": "",
  "Details": "",
  "Dictionaries": "",
  "Dictionaries enable you to translate the BTCPay Server backend into different languages.": "",
  "Dictionary": "",
  "Direct integration": "",
  "Disable public user registration": "",
  "Disable stores from using the server's email settings as backup": "",
  "Discourage search engines from indexing this site": "",
  "Display app on website root": "",
  "Display contribution ranking": "",
  "Display contribution value": "",
  "Display item selection for keypad": "",
  "Display Lightning payment amounts in Satoshis": "",
  "Display the category list": "",
  "Display the search bar": "",
  "Display Title": "",
  "Disqus Shortname": "",
  "Do not allow additional contributions after target has been reached": "",
  "Do not photograph it. Do not store it digitally.": "",
  "Do not photograph the recovery phrase, and do not store it digitally.": "",
  "Do you really want to archive the pull payment?": "",
  "Does not extend a BTCPay Server theme, fully custom": "",
  "Domain": "",
  "Domain name": "",
  "Don't create UTXO change": "",
  "Done": "",
  "Email": "",
  "Email address": "",
  "Email confirmation required": "",
  "Email confirmed?": "",
  "Emails": "",
  "Enable background animations on new payments": "",
  "Enable Disqus Comments": "",
  "Enable experimental features": "",
  "Enable LNURL": "",
  "Enable Payjoin/P2EP": "",
  "Enable public receipt page for settled invoices": "",
  "Enable public user registration": "",
  "Enable sounds on checkout page": "",
  "Enable sounds on new payments": "",
  "Enable tips": "",
  "End date": "",
  "Enter extended public key": "",
  "Enter wallet seed": "",
  "Error": "",
  "Expiration Date": "",
  "Export": "",
  "Extends the BTCPay Server Dark theme": "",
  "Extends the BTCPay Server Light theme": "",
  "Fallback": "",
  "Featured Image URL": "",
  "Fee rate (sat/vB)": "",
  "Fee will be shown for BTC and LTC onchain payments only.": "",
  "Files": "",
  "Forgot password?": "",
  "Form configuration (JSON)": "",
  "Forms": "",
  "Gap limit": "",
  "Generate": "",
  "Generate {0} Wallet": "",
  "Generate a brand-new wallet to use": "",
  "Generate API Key": "",
  "Generate Key": "",
  "Google Cloud Storage": "",
  "GRPC SSL Cipher suite (GRPC_SSL_CIPHER_SUITES)": "",
  "Has at least 1 confirmation": "",
  "Has at least 2 confirmations": "",
  "Has at least 6 confirmations": "",
  "Hide Sensitive Info": "",
  "Hot wallet": "",
  "However, <code>kraken</code> does not support the <code>BTC_CAD</code> pair. For this reason you can add a rule mapping all <code>X_CAD</code> to <code>ndax</code>, a Canadian exchange.": "",
  "However, explicitely setting specific pairs like this can be a bit difficult. Instead, you can define a rule <code>X_X</code> which will match any currency pair. The following example will use <code>kraken</code> for getting the rate of any currency pair.": "",
  "I don't have a wallet": "",
  "I have a wallet": "",
  "I have written down my recovery phrase and stored it in a secure location": "",
  "If a translation isn’t available in the new dictionary, it will be searched in the fallback.": "",
  "If you lose it or write it down incorrectly, you may permanently lose access to your funds.": "",
  "If you lose it or write it down incorrectly, you will permanently lose access to your funds.": "",
  "Image": "",
  "Import {0} Wallet": "",
  "Import an existing hardware or software wallet": "",
  "Import wallet file": "",
  "Import your public keys using our Vault application": "",
  "Input the key string manually": "",
  "Invalid currency": "",
  "Invitation URL": "",
  "Invoice currency": "",
  "Invoice expires if the full amount has not been paid after …": "",
  "Invoice Id": "",
  "Invoice metadata": "",
  "Invoices": "",
  "Is administrator?": "",
  "Is signing key": "",
  "Is unconfirmed": "",
  "It is secure, private, censorship-resistant and free.": "",
  "It is worth noting that the inverses of those pairs are automatically supported as well.<br />\n                        It means that the rule <code>USD_DOGE = 1 / DOGE_USD</code> implicitely exists.": "",
  "Item Description": "",
  "Keypad": "",
  "Labels": "",
  "Let's get started": "",
  "Lightning": "",
  "Lightning ({0})": "",
  "Lightning Address": "",
  "Lightning Balance": "",
  "Lightning node (LNURL Auth)": "",
  "Lightning Services": "",
  "LNURL Classic Mode": "",
  "Loading...": "",
  "Local File System": "",
  "Log in": "",
  "Login Codes": "",
  "Logo": "",
  "Logout": "",
  "Logs": "",
  "Maintenance": "",
  "Make Crowdfund Public": "",
  "Manage": "",
  "Manage Account": "",
  "Manage Plugins": "",
  "Master fingerprint": "",
  "Max sats": "",
  "Memo": "",
  "Metadata": "",
  "Min sats": "",
  "Minimum acceptable expiration time for BOLT11 for refunds": "",
  "Never add network fee": "",
  "New password": "",
  "Next": "",
  "No payout selected": "",
  "No scope": "",
  "Node Info": "",
  "Non-admins can access the User Creation API Endpoint": "",
  "Non-admins can create Hot Wallets for their Store": "",
  "Non-admins can import Hot Wallets for their Store": "",
  "Non-admins can use the Internal Lightning Node for their Store": "",
  "Non-admins cannot access the User Creation API Endpoint": "",
  "Not all payout methods are supported": "",
  "Not recommended": "",
  "Notification Email": "",
  "Notification URL": "",
  "Notifications": "",
  "Only enable the payment method after user explicitly chooses it": "",
  "Optional seed passphrase": "",
  "Order Id": "",
  "Override the block explorers used": "",
  "Paid invoices in the last {0} days": "",
  "Pair to": "",
  "Password": "",
  "Password (leave blank to generate invite-link)": "",
  "Pay Button": "",
  "Paying via this payment method is not supported": "",
  "PayJoin BIP21": "",
  "Payment": "",
  "Payment invalid if transactions fails to confirm … after invoice expiration": "",
  "Payment Requests": "",
  "Payments": "",
  "Payout Methods": "",
  "Payout Processors": "",
  "Payouts": "",
  "Payouts approved": "",
  "Payouts archived": "",
  "Payouts marked as paid": "",
  "Payouts Pending": "",
  "Permissions": "",
  "Please enable JavaScript for this option to be available": "",
  "Please make sure to also write down your passphrase.": "",
  "Please note that creating a hot wallet is not supported by this instance for non administrators.": "",
  "Plugin server": "",
  "Plugins": "",
  "Point of Sale": "",
  "Point of Sale Style": "",
  "Policies": "",
  "Preferred Price Source": "",
  "Print display": "",
  "Process approved payouts instantly": "",
  "Product list": "",
  "Product list with cart": "",
  "Profile Picture": "",
  "Provide the 12 or 24 word recovery seed": "",
  "PSBT content": "",
  "PSBT to combine with…": "",
  "Public Key": "",
  "Pull payment request created": "",
  "Pull Payments": "",
  "Rate Rules": "",
  "Rate script allows you to express precisely how you want to calculate rates for currency pairs.": "",
  "Rate unavailable: {0}": "",
  "Rates": "",
  "Receive": "",
  "Recent Invoices": "",
  "Recent Transactions": "",
  "Recommendation ({0})": "",
  "Recommended": "",
  "Recommended fee confirmation target blocks": "",
  "Recovery Code": "",
  "Redirect invoice to redirect url automatically after paid": "",
  "Redirect URL": "",
  "Refunds Issued": "",
  "Register": "",
  "Remember me": "",
  "Remember this machine": "",
  "Remove": "",
  "Reporting": "",
  "Request contributor data on checkout": "",
  "Request customer data on checkout": "",
  "Request Pairing": "",
  "Requests": "",
  "Required Confirmations": "",
  "Reset goal every": "",
  "Reset Password": "",
  "REST Uri": "",
  "Role": "",
  "Roles": "",
  "Root fingerprint": "",
  "Save": "",
  "Scan wallet QR code": "",
  "Scope": "",
  "Scripting": "",
  "Search engines can index this site": "",
  "Secure your recovery phrase": "",
  "Security device (FIDO2)": "",
  "Select": "",
  "Select the Default Currency during Store Creation": "",
  "Select the payout method used for refund": "",
  "Send": "",
  "Send invitation email": "",
  "Send test webhook": "",
  "Server Name": "",
  "Server Settings": "",
  "Services": "",
  "Set Password": "",
  "Set to default settings": "",
  "Set up a Lightning node": "",
  "Set up a wallet": "",
  "Settings": "",
  "Setup {0} Wallet": "",
  "Shop Name": "",
  "Shopify": "",
  "Show \"Pay in wallet\" button": "",
  "Show a timer … minutes before invoice expiration": "",
  "Show plugins in pre-release": "",
  "Show recommended fee": "",
  "Show the payment list in the public receipt page": "",
  "Show the QR code of the receipt in the public receipt page": "",
  "Show the store header": "",
  "Sign in": "",
  "Sort contribution perks by popularity": "",
  "Sounds to play when a payment is made. One sound per line": "",
  "Specify the amount and currency for the refund": "",
  "Start date": "",
  "Starting index": "",
  "Status": "",
  "Store": "",
  "Store Id": "",
  "Store Name": "",
  "Store removed successfully": "",
  "Store Settings": "",
  "Store Speed Policy": "",
  "Store successfully created": "",
  "Store Website": "",
  "Store: {0}": "",
  "Submit": "",
  "Subtract fees from amount": "",
  "Support URL": "",
  "Supported by BlueWallet, Cobo Vault, Passport and Specter DIY": "",
  "Supported Transaction Currencies": "",
  "Target Amount": "",
  "Test connection": "",
  "Test Email": "",
  "Test Results:": "",
  "Testing": "",
  "Text to display in the tip input": "",
  "Text to display on buttons allowing the user to enter a custom amount": "",
  "Text to display on each button for items with a specific price": "",
  "The amount should be more than zero": "",
  "The combination of words below are called your recovery phrase.\n            The recovery phrase allows you to access and restore your wallet.\n            Write them down on a piece of paper in the exact order:": "",
  "The following methods assume that you already have an existing&nbsp;wallet created and backed up.": "",
  "The name should be maximum 50 characters.": "",
  "The recommended price source gets chosen based on the default currency.": "",
  "The recovery phrase is a backup that allows you to restore your wallet in case of a server crash.": "",
  "The recovery phrase will also be stored on the server as a hot wallet.": "",
  "The recovery phrase will be permanently erased from the server.": "",
  "The script language is composed of several rules composed of a currency pair and a mathematic expression.\n                        The example below will use <code>kraken</code> for both <code>LTC_USD</code> and <code>BTC_USD</code> pairs.": "",
  "Theme": "",
  "There are no recent invoices.": "",
  "There are no recent transactions.": "",
  "This store is ready to accept transactions, good job!": "",
  "This store will still be accessible to users sharing it": "",
  "Tip percentage amounts (comma separated)": "",
  "To start accepting payments, set up a wallet or a Lightning node.": "",
  "Transaction": "",
  "Translations": "",
  "Two-Factor Authentication": "",
  "Unarchive this store": "",
  "Unify on-chain and lightning payment URL/QR code": "",
  "Update Password": "",
  "Update Webhook": "",
  "Upload a file exported from your wallet": "",
  "Upload PSBT from file…": "",
  "Url of the Dynamic DNS service you are using": "",
  "Use custom node": "",
  "Use custom theme": "",
  "Use internal node": "",
  "Use SSL": "",
  "User can input custom amount": "",
  "User can input discount in %": "",
  "Users": "",
  "Using the BTCPay Server internal node for this store requires no further configuration. Click the save button below to start accepting Bitcoin through the Lightning Network.": "",
  "UTXOs to spend from": "",
  "Verification Code": "",
  "View All": "",
  "View-Only Wallet File": "",
  "Wallet Balance": "",
  "Wallet file": "",
  "Wallet file content": "",
  "Wallet Keys File": "",
  "Wallet Password": "",
  "Wallet's private key is erased from the server. Higher security. To spend, you have to manually input the private key or import it into an external wallet.": "",
  "Wallet's private key is stored on the server. Spending the funds you received is convenient. To minimize the risk of theft, regularly withdraw funds to a different wallet.": "",
  "Wallets": "",
  "Watch-only wallet": "",
  "Webhooks": "",
  "Welcome to {0}": "",
  "With <code>DOGE_USD</code> will be expanded to <code>bitpay(DOGE_BTC) * kraken(BTC_USD)</code>. And <code>DOGE_CAD</code> will be expanded to <code>bitpay(DOGE_BTC) * ndax(BTC_CAD)</code>. <br />\n                        However, we advise you to write it that way to increase coverage so that <code>DOGE_BTC</code> is also supported:": "",
  "You must enable at least one payment method before creating a payout.": "",
  "You must enable at least one payment method before creating a pull payment.": "",
  "You need at least one payout method": "",
  "You really should not type your seed into a device that is connected to the internet.": "",
  "Your dynamic DNS hostname": "",
  "Your instance administrator has disabled the use of the Internal node for non-admin users.": "",
  "Zero Confirmation": ""
}
""";
            Default = Translations.CreateFromJson(knownTranslations);
            Default = new Translations(new KeyValuePair<string, string>[]
            {
                // You can add additional hard coded default here
                // KeyValuePair.Create("key1", "value")
                // KeyValuePair.Create("key2", "value")
            }, Default);
        }

        /// <summary>
        /// Translations which are already in the Default aren't saved into database.
        /// This allows us to automatically update the english version if the translations didn't changed.
        /// 
        /// We only save into database the key/values that differ from Default
        /// </summary>
        public static Translations Default;
        public readonly static string DefaultLanguage = "English";
    }
}
