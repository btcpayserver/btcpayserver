
# GreenField API Authorization Flow

The GreenField API allows two modes of authentication to its endpoints: Basic Auth and API keys.

## Basic auth
Basic auth allows you to seamlessly integrate with BTCPay Server's user system using only a traditional user/password login form. This is however a security risk if the application is a third party as they will receive your credentials in plain text and will be able to access your full account.

## API Keys
BTCPay Server's Greenfield API also allows users to generate API keys with [specific permissions](https://docs.btcpayserver.org/API/Greenfield/v1/#section/Authentication/API_Key). **If you are integrating BTCPay Server into your third-party application, this is the recommended way.**

### Manually create an API key
Users can create a new API key in the BTCPay Server UI under `Account` -> `Manage account` -> `API keys`

### Create API keys over the API itself

A user can create an API key for themselves using the [Create API Key endpoint](https://docs.btcpayserver.org/API/Greenfield/v1/#operation/APIKeys_CreateAPIKey) via Basic Auth or an unrestricted API key. Server administrators can create API keys for any user using the [Create API key for user endpoint](https://docs.btcpayserver.org/API/Greenfield/v1/#operation/ApiKeys_CreateUserApiKey).

### Interactive API key setup flow

Asking a user to generate a dedicated API key, with a specific set of permissions manually can be a bad UX experience. For this scenario, we have the [Authorize User UI](https://docs.btcpayserver.org/API/Greenfield/v1/#tag/Authorization). This allows external applications to request the user to generate an API key with a specific set of permissions by simply generating a URL to BTCPay Server and redirecting the user to it.
Additionally, there are 2 optional parameters to the endpoint which allow a more seamless integration:
* if `redirect` is specified, once the API key is created, BTCPay Server redirects the user via a POST submission to the specified `redirect` URL, with a json body containing the API key, user id, and permissions granted.
* if `applicationIdentifier` is specified (along with `redirect`), BTCPay Server will check if there is an existing API key associated with the user that also has this application identifier, redirect host AND the permissions required match. `applicationIdentifier` is ignored if `redirect` is not specified.

Some examples of a generated Authorize URL:
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize` - A simplistic request, where no permission is requested. Useful to prove that a user exists on a specific BTCPay Server instance.
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize?applicationName=Your%20Application` - Indicates that the API key is being generated for `Your Application`
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize?applicationName=Your%20Application&redirect=http://gozo.com` - Redirects the user via a POST to `http://gozo.com` with a JSON body containing the API key and its info.
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize?applicationName=Your%20Application&redirect=http://gozo.com&applicationIdentifier=gozo` - Attempts to match a previously created API key based on the app identifier, domain and permissions and is prompted.
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize?permissions=btcpay.store.cancreateinvoice&permissions=btcpay.store.canviewinvoices` - A request asking for permissions to create and view invoices on all stores available to the user
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize?permissions=btcpay.store.cancreateinvoice&permissions=btcpay.store.canviewinvoices&selectiveStores=true` - A request asking for permissions to create and view invoices on stores but also allows the user to choose which stores the application will have the permission to.
* `https://mainnet.demo.btcpayserver.org/api-keys/authorize?permissions=btcpay.store.cancreateinvoice&permissions=btcpay.store.canviewinvoices&strict=false` - A request asking for permissions but allows the user to remove or add to the requested permission list.
