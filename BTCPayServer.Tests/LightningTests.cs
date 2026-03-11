using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;
using CreateInvoiceRequest = BTCPayServer.Client.Models.CreateInvoiceRequest;
using BTCPayServer.Data;
using LightningAddressData = BTCPayServer.Client.Models.LightningAddressData;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class LightningTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
    [Fact(Timeout = 60 * 20 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanAccessInvoiceLightningPaymentMethodDetails()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        user.RegisterLightningNode("BTC", LightningConnectionType.CLightning);

        var client = await user.CreateClient(Policies.Unrestricted);
        var invoices = new Task<Client.Models.InvoiceData>[5];

        // Create invoices
        for (int i = 0; i < invoices.Length; i++)
        {
            invoices[i] = client.CreateInvoice(user.StoreId,
                new CreateInvoiceRequest
                {
                    Currency = "USD",
                    Amount = 0.1m,
                    Checkout = new CreateInvoiceRequest.CheckoutOptions
                    {
                        PaymentMethods = new[] { "BTC-LN" },
                        DefaultPaymentMethod = "BTC-LN"
                    }
                });
        }

        var pm = new InvoicePaymentMethodDataModel[invoices.Length];
        for (int i = 0; i < invoices.Length; i++)
        {
            pm[i] = Assert.Single(await client.GetInvoicePaymentMethods(user.StoreId, (await invoices[i]).Id));
            Assert.True(pm[i].AdditionalData.HasValues);
        }

        // Pay them all at once
        Task<PayResponse>[] payResponses = new Task<PayResponse>[invoices.Length];
        for (int i = 0; i < invoices.Length; i++)
        {
            payResponses[i] = tester.CustomerLightningD.Pay(pm[i].Destination);
        }

        // Checking the results
        for (int i = 0; i < invoices.Length; i++)
        {
            var resp = await payResponses[i];
            Assert.Equal(PayResult.Ok, resp.Result);
            Assert.NotNull(resp.Details.PaymentHash);
            Assert.NotNull(resp.Details.Preimage);
            await TestUtils.EventuallyAsync(async () =>
            {
                pm[i] = Assert.Single(await client.GetInvoicePaymentMethods(user.StoreId, (await invoices[i]).Id));
                Assert.True(pm[i].AdditionalData.HasValues);
                Assert.Equal(resp.Details.PaymentHash.ToString(), ((JObject)pm[i].AdditionalData).GetValue("paymentHash"));
                Assert.Equal(resp.Details.Preimage.ToString(), ((JObject)pm[i].AdditionalData).GetValue("preimage"));
            });
        }
    }

    [Fact(Timeout = 60 * 20 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanUseLightningAPI()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        user.RegisterLightningNode("BTC", LightningConnectionType.CLightning, false);

        var merchant = tester.NewAccount();
        await merchant.GrantAccessAsync(true);
        merchant.RegisterLightningNode("BTC", LightningConnectionType.LndREST);
        var merchantClient = await merchant.CreateClient($"{Policies.CanUseLightningNodeInStore}:{merchant.StoreId}");
        var merchantInvoice = await merchantClient.CreateLightningInvoice(merchant.StoreId, "BTC",
            new CreateLightningInvoiceRequest(LightMoney.Satoshis(1_000), "hey", TimeSpan.FromSeconds(60)));
        Assert.NotNull(merchantInvoice.Id);
        Assert.NotNull(merchantInvoice.PaymentHash);
        Assert.Equal(merchantInvoice.Id, merchantInvoice.PaymentHash);

        var client = await user.CreateClient(Policies.CanUseInternalLightningNode);
        // Not permission for the store!
        await AssertEx.AssertApiError("missing-permission", () => client.GetLightningNodeChannels(user.StoreId, "BTC"));
        var invoiceData = await client.CreateLightningInvoice("BTC", new CreateLightningInvoiceRequest()
        {
            Amount = LightMoney.Satoshis(1000),
            Description = "lol",
            Expiry = TimeSpan.FromSeconds(400),
            PrivateRouteHints = false
        });
        Assert.NotNull(await client.GetLightningInvoice("BTC", invoiceData.Id));

        // check list for internal node
        var invoices = await client.GetLightningInvoices("BTC");
        var pendingInvoices = await client.GetLightningInvoices("BTC", true);
        Assert.NotEmpty(invoices);
        Assert.Contains(invoices, i => i.Id == invoiceData.Id);
        Assert.NotEmpty(pendingInvoices);
        Assert.Contains(pendingInvoices, i => i.Id == invoiceData.Id);

        client = await user.CreateClient($"{Policies.CanUseLightningNodeInStore}:{user.StoreId}");
        // Not permission for the server
        await AssertEx.AssertApiError("missing-permission", () => client.GetLightningNodeChannels("BTC"));

        var data = await client.GetLightningNodeChannels(user.StoreId, "BTC");
        Assert.Equal(2, data.Count());
        BitcoinAddress.Create(await client.GetLightningDepositAddress(user.StoreId, "BTC"), Network.RegTest);

        invoiceData = await client.CreateLightningInvoice(user.StoreId, "BTC", new CreateLightningInvoiceRequest()
        {
            Amount = LightMoney.Satoshis(1000),
            Description = "lol",
            Expiry = TimeSpan.FromSeconds(400),
            PrivateRouteHints = false
        });

        Assert.NotNull(await client.GetLightningInvoice(user.StoreId, "BTC", invoiceData.Id));

        // check pending list
        var merchantPendingInvoices = await merchantClient.GetLightningInvoices(merchant.StoreId, "BTC", true);
        Assert.NotEmpty(merchantPendingInvoices);
        Assert.Contains(merchantPendingInvoices, i => i.Id == merchantInvoice.Id);

        var payResponse = await client.PayLightningInvoice(user.StoreId, "BTC", new PayLightningInvoiceRequest
        {
            BOLT11 = merchantInvoice.BOLT11
        });
        Assert.Equal(merchantInvoice.BOLT11, payResponse.BOLT11);
        Assert.Equal(LightningPaymentStatus.Complete, payResponse.Status);
        Assert.NotNull(payResponse.Preimage);
        Assert.NotNull(payResponse.FeeAmount);
        Assert.NotNull(payResponse.TotalAmount);
        Assert.NotNull(payResponse.PaymentHash);

        // check the get invoice response
        var merchInvoice = await merchantClient.GetLightningInvoice(merchant.StoreId, "BTC", merchantInvoice.Id);
        Assert.NotNull(merchInvoice);
        Assert.NotNull(merchInvoice.Preimage);
        Assert.NotNull(merchInvoice.PaymentHash);
        Assert.Equal(payResponse.Preimage, merchInvoice.Preimage);
        Assert.Equal(payResponse.PaymentHash, merchInvoice.PaymentHash);

        await Assert.ThrowsAsync<GreenfieldValidationException>(async () => await client.PayLightningInvoice(user.StoreId, "BTC",
            new PayLightningInvoiceRequest()
            {
                BOLT11 = "lol"
            }));

        var validationErr = await Assert.ThrowsAsync<GreenfieldValidationException>(async () => await client.CreateLightningInvoice(user.StoreId, "BTC",
            new CreateLightningInvoiceRequest()
            {
                Amount = -1,
                Expiry = TimeSpan.FromSeconds(-1),
                Description = null
            }));
        Assert.Equal(2, validationErr.ValidationErrors.Length);

        var invoice = await merchantClient.GetLightningInvoice(merchant.StoreId, "BTC", merchantInvoice.Id);
        Assert.NotNull(invoice.PaidAt);
        Assert.NotNull(invoice.PaymentHash);
        Assert.NotNull(invoice.Preimage);
        Assert.Equal(LightMoney.Satoshis(1000), invoice.Amount);

        // check list for store with paid invoice
        var merchantInvoices = await merchantClient.GetLightningInvoices(merchant.StoreId, "BTC");
        Assert.NotEmpty(merchantInvoices);
        merchantPendingInvoices = await merchantClient.GetLightningInvoices(merchant.StoreId, "BTC", true);
        Assert.True(merchantPendingInvoices.Length < merchantInvoices.Length);
        Assert.All(merchantPendingInvoices, m => Assert.Equal(LightningInvoiceStatus.Unpaid, m.Status));
        // if the test ran too many times the invoice might be on a later page
        if (merchantInvoices.Length < 100)
            Assert.Contains(merchantInvoices, i => i.Id == merchantInvoice.Id);

        // Amount received might be bigger because of internal implementation shit from lightning
        Assert.True(LightMoney.Satoshis(1000) <= invoice.AmountReceived);

        // check payments list for store node
        var payments = await client.GetLightningPayments(user.StoreId, "BTC");
        Assert.NotEmpty(payments);
        Assert.Contains(payments, i => i.BOLT11 == merchantInvoice.BOLT11);

        // Node info
        var info = await client.GetLightningNodeInfo(user.StoreId, "BTC");
        Assert.Single(info.NodeURIs);
        Assert.NotEqual(0, info.BlockHeight);

        // Disable for now see #6518
        //// balance
        //await TestUtils.EventuallyAsync(async () =>
        //{
        //    var balance = await client.GetLightningNodeBalance(user.StoreId, "BTC");
        //    var localBalance = balance.OffchainBalance.Local.ToDecimal(LightMoneyUnit.BTC);
        //    var histogram = await client.GetLightningNodeHistogram(user.StoreId, "BTC");
        //    Assert.Equal(histogram.Balance, histogram.Series.Last());
        //    Assert.Equal(localBalance, histogram.Balance);
        //    Assert.Equal(localBalance, histogram.Series.Last());
        //});

        // As admin, can use the internal node through our store.
        await user.MakeAdmin();
        await user.RegisterInternalLightningNodeAsync("BTC");
        await client.GetLightningNodeInfo(user.StoreId, "BTC");
        // But if not admin anymore, nope
        await user.MakeAdmin(false);
        await AssertEx.AssertPermissionError("btcpay.server.canuseinternallightningnode", () => client.GetLightningNodeInfo(user.StoreId, "BTC"));
        // However, even as a guest, you should be able to create an invoice
        var guest = tester.NewAccount();
        await guest.GrantAccessAsync();
        await user.AddGuest(guest.UserId);
        client = await guest.CreateClient(Policies.CanCreateLightningInvoiceInStore);
        await client.CreateLightningInvoice(user.StoreId, "BTC", new CreateLightningInvoiceRequest()
        {
            Amount = LightMoney.Satoshis(1000),
            Description = "lol",
            Expiry = TimeSpan.FromSeconds(600),
        });
        client = await guest.CreateClient(Policies.CanUseLightningNodeInStore);
        // Can use lightning node is only granted to store's owner
        await AssertEx.AssertPermissionError("btcpay.store.canuselightningnode", () => client.GetLightningNodeInfo(user.StoreId, "BTC"));

        // balance and histogram should not be accessible with view only clients
        await AssertEx.AssertPermissionError("btcpay.store.canuselightningnode", () => client.GetLightningNodeBalance(user.StoreId, "BTC"));
        await AssertEx.AssertPermissionError("btcpay.store.canuselightningnode", () => client.GetLightningNodeHistogram(user.StoreId, "BTC"));
    }

    [Fact(Timeout = 60 * 20 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanUseLightningAPI2()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);

        var types = new[] { LightningConnectionType.LndREST, LightningConnectionType.CLightning };
        foreach (var type in types)
        {
            user.RegisterLightningNode("BTC", type);
            var client = await user.CreateClient("btcpay.store.cancreatelightninginvoice");
            var amount = LightMoney.Satoshis(1000);
            var expiry = TimeSpan.FromSeconds(600);

            var invoice = await client.CreateLightningInvoice(user.StoreId, "BTC", new CreateLightningInvoiceRequest
            {
                Amount = amount,
                Expiry = expiry,
                Description = "Hashed description",
                DescriptionHashOnly = true
            });
            var bolt11 = BOLT11PaymentRequest.Parse(invoice.BOLT11, Network.RegTest);
            Assert.NotNull(bolt11.DescriptionHash);
            Assert.Null(bolt11.ShortDescription);

            invoice = await client.CreateLightningInvoice(user.StoreId, "BTC", new CreateLightningInvoiceRequest
            {
                Amount = amount,
                Expiry = expiry,
                Description = "Standard description",
            });
            bolt11 = BOLT11PaymentRequest.Parse(invoice.BOLT11, Network.RegTest);
            Assert.Null(bolt11.DescriptionHash);
            Assert.NotNull(bolt11.ShortDescription);
        }
    }

    [Fact(Timeout = 60 * 2 * 1000)]
    [Trait("Lightning", "Lightning")]
    [Trait("Integration", "Integration")]
    public async Task LightningNetworkPaymentMethodAPITests()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var admin = tester.NewAccount();
        await admin.GrantAccessAsync(true);
        var admin2 = tester.NewAccount();
        await admin2.GrantAccessAsync(true);
        var adminClient = await admin.CreateClient(Policies.CanModifyStoreSettings);
        var admin2Client = await admin2.CreateClient(Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings);
        var viewOnlyClient = await admin.CreateClient(Policies.CanViewStoreSettings);
        var store = await adminClient.GetStore(admin.StoreId);

        Assert.Empty(await adminClient.GetStorePaymentMethods(store.Id));
        await AssertEx.AssertHttpError(403, async () =>
        {
            await viewOnlyClient.UpdateStorePaymentMethod(store.Id, "BTC-LN", new UpdatePaymentMethodRequest());
        });
        await AssertEx.AssertHttpError(404, async () =>
        {
            await adminClient.GetStorePaymentMethod(store.Id, "BTC-LN");
        });
        await admin.RegisterLightningNodeAsync("BTC", false);

        var method = await adminClient.GetStorePaymentMethod(store.Id, "BTC-LN");
        Assert.Null(method.Config);
        method = await adminClient.GetStorePaymentMethod(store.Id, "BTC-LN", includeConfig: true);
        Assert.NotNull(method.Config);
        await AssertEx.AssertHttpError(403, async () =>
        {
            await viewOnlyClient.RemoveStorePaymentMethod(store.Id, "BTC-LN");
        });
        await adminClient.RemoveStorePaymentMethod(store.Id, "BTC-LN");
        await AssertEx.AssertHttpError(404, async () =>
        {
            await adminClient.GetStorePaymentMethod(store.Id, "BTC-LN");
        });


        // Let's verify that the admin client can't change LN to unsafe connection strings without modify server settings rights
        foreach (var forbidden in new[]
                 {
                     "type=clightning;server=tcp://127.0.0.1",
                     "type=clightning;server=tcp://test",
                     "type=clightning;server=tcp://test.lan",
                     "type=clightning;server=tcp://test.local",
                     "type=clightning;server=tcp://192.168.1.2",
                     "type=clightning;server=unix://8.8.8.8",
                     "type=clightning;server=unix://[::1]",
                     "type=clightning;server=unix://[0:0:0:0:0:0:0:1]",
                 })
        {
            var ex = await AssertEx.AssertValidationError(new[] { "ConnectionString" }, async () =>
            {
                await adminClient.UpdateStorePaymentMethod(store.Id, "BTC-LN", new UpdatePaymentMethodRequest()
                {
                    Config = new JObject()
                    {
                        ["connectionString"] = forbidden
                    },
                    Enabled = true
                });
            });
            Assert.Contains("btcpay.server.canmodifyserversettings", ex.Message);
            // However, the other client should work because he has `btcpay.server.canmodifyserversettings`
            await admin2Client.UpdateStorePaymentMethod(admin2.StoreId, "BTC-LN", new UpdatePaymentMethodRequest()
            {
                Config = new JObject()
                {
                    ["connectionString"] = forbidden
                },
                Enabled = true
            });
        }

        // Allowed ip should be ok
        await adminClient.UpdateStorePaymentMethod(store.Id, "BTC-LN", new UpdatePaymentMethodRequest()
        {
            Config = new JObject()
            {
                ["connectionString"] = "type=clightning;server=tcp://8.8.8.8"
            },
            Enabled = true
        });
        // If we strip the admin's right, he should not be able to set unsafe anymore, even if the API key is still valid
        await admin2.MakeAdmin(false);
        await AssertEx.AssertValidationError(new[] { "ConnectionString" }, async () =>
        {
            await admin2Client.UpdateStorePaymentMethod(admin2.StoreId, "BTC-LN", new UpdatePaymentMethodRequest()
            {
                Config = new JObject()
                {
                    ["connectionString"] = "type=clightning;server=tcp://127.0.0.1"
                },
                Enabled = true
            });
        });

        var settings = (await tester.PayTester.GetService<SettingsRepository>().GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
        settings.AllowLightningInternalNodeForAll = false;
        await tester.PayTester.GetService<SettingsRepository>().UpdateSetting(settings);
        var nonAdminUser = tester.NewAccount();
        await nonAdminUser.GrantAccessAsync();
        var nonAdminUserClient = await nonAdminUser.CreateClient(Policies.CanModifyStoreSettings);

        await AssertEx.AssertHttpError(404, async () =>
        {
            await nonAdminUserClient.GetStorePaymentMethod(nonAdminUser.StoreId, "BTC-LN");
        });
        await AssertEx.AssertPermissionError("btcpay.server.canuseinternallightningnode", () => nonAdminUserClient.UpdateStorePaymentMethod(
            nonAdminUser.StoreId,
            "BTC-LN", new UpdatePaymentMethodRequest()
            {
                Enabled = method.Enabled,
                Config = new JObject()
                {
                    ["internalNodeRef"] = "Internal Node"
                }
            }));

        settings = await tester.PayTester.GetService<SettingsRepository>().GetSettingAsync<PoliciesSettings>() ?? new();
        settings.AllowLightningInternalNodeForAll = true;
        await tester.PayTester.GetService<SettingsRepository>().UpdateSetting(settings);

        await nonAdminUserClient.UpdateStorePaymentMethod(nonAdminUser.StoreId, "BTC-LN", new UpdatePaymentMethodRequest()
        {
            Enabled = method.Enabled,
            Config = new JObject()
            {
                ["internalNodeRef"] = "Internal Node"
            }
        });

        // NonAdmin can't set to internal node in AllowLightningInternalNodeForAll is false, but can do other connection string
        settings = (await tester.PayTester.GetService<SettingsRepository>().GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
        settings.AllowLightningInternalNodeForAll = false;
        await tester.PayTester.GetService<SettingsRepository>().UpdateSetting(settings);
        await nonAdminUserClient.UpdateStorePaymentMethod(nonAdminUser.StoreId, "BTC-LN", new UpdatePaymentMethodRequest()
        {
            Enabled = true,
            Config = new JObject()
            {
                ["connectionString"] = "type=clightning;server=tcp://8.8.8.8"
            }
        });
        await AssertEx.AssertPermissionError("btcpay.server.canuseinternallightningnode", () => nonAdminUserClient.UpdateStorePaymentMethod(
            nonAdminUser.StoreId,
            "BTC-LN", new UpdatePaymentMethodRequest()
            {
                Enabled = true,
                Config = new JObject()
                {
                    ["connectionString"] = "Internal Node"
                }
            }));
        // NonAdmin add admin as owner of the store
        await nonAdminUser.AddOwner(admin.UserId);
        // Admin turn on Internal node
        adminClient = await admin.CreateClient(Policies.CanModifyStoreSettings, Policies.CanUseInternalLightningNode);
        var data = await adminClient.UpdateStorePaymentMethod(nonAdminUser.StoreId, "BTC-LN", new UpdatePaymentMethodRequest()
        {
            Enabled = method.Enabled,
            Config = new JObject()
            {
                ["connectionString"] = "Internal Node"
            }
        });
        Assert.NotNull(data);
        Assert.NotNull(data.Config["internalNodeRef"]?.Value<string>());
        // Make sure that the nonAdmin can toggle enabled, ConnectionString unchanged.
        await nonAdminUserClient.UpdateStorePaymentMethod(nonAdminUser.StoreId, "BTC-LN", new UpdatePaymentMethodRequest()
        {
            Enabled = !data.Enabled,
            Config = new JObject()
            {
                ["connectionString"] = "Internal Node"
            }
        });
    }


    [Fact]
    [Trait("Integration", "Integration")]
    public async Task StoreLightningAddressesAPITests()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var admin = tester.NewAccount();
        await admin.GrantAccessAsync(true);
        var adminClient = await admin.CreateClient(Policies.Unrestricted);
        var store = await adminClient.GetStore(admin.StoreId);

        Assert.Empty(await adminClient.GetStorePaymentMethods(store.Id));
        var store2 = (await adminClient.CreateStore(new CreateStoreRequest() { Name = "test2" })).Id;
        var address1 = Guid.NewGuid().ToString("n").Substring(0, 8);
        var address2 = Guid.NewGuid().ToString("n").Substring(0, 8);
        var address3 = Guid.NewGuid().ToString("n").Substring(0, 8);

        Assert.Empty(await adminClient.GetStoreLightningAddresses(store.Id));
        Assert.Empty(await adminClient.GetStoreLightningAddresses(store2));
        await adminClient.AddOrUpdateStoreLightningAddress(store.Id, address1, new LightningAddressData());

        await adminClient.AddOrUpdateStoreLightningAddress(store.Id, address1, new LightningAddressData()
        {
            Max = 1
        });
        await AssertEx.AssertApiError("username-already-used", async () =>
        {
            await adminClient.AddOrUpdateStoreLightningAddress(store2, address1, new LightningAddressData());
        });
        Assert.Equal(1, Assert.Single(await adminClient.GetStoreLightningAddresses(store.Id)).Max);
        Assert.Empty(await adminClient.GetStoreLightningAddresses(store2));

        await adminClient.AddOrUpdateStoreLightningAddress(store2, address2, new LightningAddressData());

        Assert.Single(await adminClient.GetStoreLightningAddresses(store.Id));
        Assert.Single(await adminClient.GetStoreLightningAddresses(store2));
        await AssertEx.AssertHttpError(404, async () =>
        {
            await adminClient.RemoveStoreLightningAddress(store2, address1);
        });
        await adminClient.RemoveStoreLightningAddress(store2, address2);

        Assert.Empty(await adminClient.GetStoreLightningAddresses(store2));

        var store3 = (await adminClient.CreateStore(new CreateStoreRequest { Name = "test3" })).Id;
        Assert.Empty(await adminClient.GetStoreLightningAddresses(store3));
        var metadata = JObject.FromObject(new { test = 123 });
        await adminClient.AddOrUpdateStoreLightningAddress(store3, address3, new LightningAddressData
        {
            InvoiceMetadata = metadata
        });
        var lnAddresses = await adminClient.GetStoreLightningAddresses(store3);
        Assert.Single(lnAddresses);
        Assert.Equal(metadata, lnAddresses[0].InvoiceMetadata);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    [Trait("Lightning", "Lightning")]
    public async Task CanManageLightningNode()
    {
        await using var s = CreatePlaywrightTester();
        s.Server.ActivateLightning();
        await s.StartAsync();
        await s.Server.EnsureChannelsSetup();
        await s.RegisterNewUser(true);
        var (storeName, _) = await s.CreateNewStore();

        // Check status in navigation
        await s.Page.Locator("#menu-item-LightningSettings-BTC .btcpay-status--pending").WaitForAsync();

        // Set up the LN node
        await s.AddLightningNode();
        await s.Page.Locator("#menu-item-Lightning-BTC .btcpay-status--enabled").WaitForAsync();

        // Check public node info for availability
        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.ClickAsync("#PublicNodeInfo");
                     }))
        {
            await Expect(s.Page.Locator(".store-name")).ToHaveTextAsync(storeName);
            await Expect(s.Page.Locator("#LightningNodeTitle")).ToHaveTextAsync("BTC Lightning Node");
            await Expect(s.Page.Locator("#LightningNodeStatus")).ToHaveTextAsync("Online");
            await s.Page.Locator(".btcpay-status--enabled").WaitForAsync();
            await Expect(s.Page.Locator("#LightningNodeUrlClearnet")).ToHaveCountAsync(0);
            await s.GoToUrl(s.Page.Url + "?showLocal=true");
            await Expect(s.Page.Locator("#LightningNodeUrlClearnet")).ToHaveCountAsync(1);
        }

        // Set wrong node connection string to simulate offline node
        await s.GoToLightningSettings();
        await s.Page.ClickAsync("#SetupLightningNodeLink");
        await s.Page.ClickAsync("label[for=\"LightningNodeType-Custom\"]");
        await s.Page.Locator("#ConnectionString").WaitForAsync();
        await s.Page.Locator("#ConnectionString").ClearAsync();
        await s.Page.FillAsync("#ConnectionString", "type=lnd-rest;server=https://doesnotwork:8080/");
        await s.Page.ClickAsync("#test");
        await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);
        await s.ClickPagePrimary();
        await s.FindAlertMessage(partialText: "BTC Lightning node updated.");

        // Check offline state is communicated in the nav item
        await s.Page.Locator("#menu-item-Lightning-BTC .btcpay-status--disabled").WaitForAsync();

        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.ClickAsync("#PublicNodeInfo");
                     }))
        {
            await Expect(s.Page.Locator(".store-name")).ToHaveTextAsync(storeName);
            await Expect(s.Page.Locator("#LightningNodeTitle")).ToHaveTextAsync("BTC Lightning Node");
            await Expect(s.Page.Locator("#LightningNodeStatus")).ToHaveTextAsync("Unavailable");
            await s.Page.Locator(".btcpay-status--disabled").WaitForAsync();
            await Expect(s.Page.Locator("#LightningNodeUrlClearnet")).ToHaveCountAsync(0);
            await s.GoToUrl(s.Page.Url + "?showLocal=true");
            await Expect(s.Page.Locator("#LightningNodeUrlClearnet")).ToHaveCountAsync(0);
        }
    }

    [Fact(Timeout = 60 * 2 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanSendLightningPaymentCLightning()
    {
        await ProcessLightningPayment(LightningConnectionType.CLightning);
    }

    [Fact(Timeout = 60 * 2 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanSendLightningPaymentLnd()
    {
        await ProcessLightningPayment(LightningConnectionType.LndREST);
    }

    async Task ProcessLightningPayment(string type)
    {
        // For easier debugging and testing
        // LightningLikePaymentHandler.LIGHTNING_TIMEOUT = int.MaxValue;

        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        user.GrantAccess(true);
        user.RegisterLightningNode("BTC", type);
        user.RegisterDerivationScheme("BTC");

        await CanSendLightningPaymentCore(tester, user);

        await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => CanSendLightningPaymentCore(tester, user))
            .ToArray());
    }

    async Task CanSendLightningPaymentCore(ServerTester tester, TestAccount user)
    {
        var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice()
        {
            Price = 0.01m,
            Currency = "USD",
            PosData = "posData",
            OrderId = "orderId",
            ItemDesc = "Some description"
        });
        await Task.Delay(TimeSpan.FromMilliseconds(1000)); // Give time to listen the new invoices
        TestLogs.LogInformation($"Trying to send Lightning payment to {invoice.Id}");
        await tester.SendLightningPaymentAsync(invoice);
        TestLogs.LogInformation($"Lightning payment to {invoice.Id} is sent");
        await TestUtils.EventuallyAsync(async () =>
        {
            var localInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
            Assert.Equal("complete", localInvoice.Status);
            // C-Lightning may overpay for privacy
            Assert.Contains(localInvoice.ExceptionStatus.ToString(), new[] { "False", "paidOver" });
        });
    }

    [Fact(Timeout = 60 * 2 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task EnsureNewLightningInvoiceOnPartialPayment()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        await user.RegisterDerivationSchemeAsync("BTC");
        await user.RegisterLightningNodeAsync("BTC", LightningConnectionType.CLightning);
        await user.SetNetworkFeeMode(NetworkFeeMode.Never);
        await user.ModifyGeneralSettings(p => p.SpeedPolicy = SpeedPolicy.HighSpeed);
        var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(0.0001m, "BTC"));
        await tester.WaitForEvent<InvoiceNewPaymentDetailsEvent>(async () =>
        {
            await tester.ExplorerNode.SendToAddressAsync(
                BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest), Money.Coins(0.00005m), new NBitcoin.RPC.SendToAddressParameters()
                {
                    Replaceable = false
                });
        }, e => e.InvoiceId == invoice.Id && e.PaymentMethodId == PaymentTypes.LN.GetPaymentMethodId("BTC"));
        Invoice newInvoice = null;
        await TestUtils.EventuallyAsync(async () =>
        {
            await Task.Delay(1000); // wait a bit for payment to process before fetching new invoice
            newInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
            var newBolt11 = newInvoice.CryptoInfo.First(o => o.PaymentUrls.BOLT11 != null).PaymentUrls.BOLT11;
            var oldBolt11 = invoice.CryptoInfo.First(o => o.PaymentUrls.BOLT11 != null).PaymentUrls.BOLT11;
            Assert.NotEqual(newBolt11, oldBolt11);
            Assert.Equal(newInvoice.BtcDue.ToDecimal(MoneyUnit.BTC),
                BOLT11PaymentRequest.Parse(newBolt11, Network.RegTest).MinimumAmount.ToDecimal(LightMoneyUnit.BTC));
        }, 40000);

        TestLogs.LogInformation(
            $"Paying invoice {newInvoice.Id} remaining due amount {newInvoice.BtcDue.GetValue((BTCPayNetwork)tester.DefaultNetwork)} via lightning");
        var evt = await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
        {
            await tester.SendLightningPaymentAsync(newInvoice);
        }, evt => evt.InvoiceId == invoice.Id);

        var fetchedInvoice = await tester.PayTester.InvoiceRepository.GetInvoice(evt.InvoiceId);
        Assert.Equal(InvoiceStatus.Settled, fetchedInvoice.Status);
        Assert.Equal(InvoiceExceptionStatus.None, fetchedInvoice.ExceptionStatus);

        //BTCPay will attempt to cancel previous bolt11 invoices so that there are less weird edge case scenarios
        TestLogs.LogInformation($"Attempting to pay invoice {invoice.Id} original full amount bolt11 invoice");
        var res = await tester.SendLightningPaymentAsync(invoice);
        Assert.Equal(PayResult.Error, res.Result);

        //NOTE: Eclair does not support cancelling invoice so the below test case would make sense for it
        // TestLogs.LogInformation($"Paying invoice {invoice.Id} original full amount bolt11 invoice ");
        // evt = await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
        // {
        //     await tester.SendLightningPaymentAsync(invoice);
        // }, evt => evt.InvoiceId == invoice.Id);
        // Assert.Equal(evt.InvoiceId, invoice.Id);
        // fetchedInvoice = await tester.PayTester.InvoiceRepository.GetInvoice(evt.InvoiceId);
        // Assert.Equal(3, fetchedInvoice.Payments.Count);
    }

    [Fact(Timeout = 60 * 2 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanSetLightningServer()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        var storeController = user.GetController<UIStoresController>();
        var storeResponse = await storeController.GeneralSettings(user.StoreId);
        Assert.IsType<ViewResult>(storeResponse);
        Assert.IsType<ViewResult>(storeController.SetupLightningNode(user.StoreId, "BTC"));

        await storeController.SetupLightningNode(user.StoreId, new LightningNodeViewModel
        {
            ConnectionString = $"type=charge;server={tester.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true",
            SkipPortTest = true // We can't test this as the IP can't be resolved by the test host :(
        }, "test", "BTC");
        Assert.False(storeController.TempData.ContainsKey(WellKnownTempData.ErrorMessage));
        storeController.TempData.Clear();
        Assert.True(storeController.ModelState.IsValid);

        Assert.IsType<RedirectToActionResult>(await storeController.SetupLightningNode(user.StoreId,
            new LightningNodeViewModel
            {
                ConnectionString = $"type=charge;server={tester.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true"
            }, "save", "BTC"));

        // Make sure old connection string format does not work
        Assert.IsType<RedirectToActionResult>(await storeController.SetupLightningNode(user.StoreId,
            new LightningNodeViewModel { ConnectionString = tester.MerchantCharge.Client.Uri.AbsoluteUri },
            "save", "BTC"));

        storeResponse = storeController.LightningSettings(user.StoreId, "BTC");
        var storeVm =
            Assert.IsType<LightningSettingsViewModel>(Assert
                .IsType<ViewResult>(storeResponse).Model);
        Assert.NotEmpty(storeVm.ConnectionString);
    }

    [Fact(Timeout = 60 * 2 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanSetPaymentMethodLimitsLightning()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        var cryptoCode = "BTC";
        user.GrantAccess(true);
        user.RegisterLightningNode(cryptoCode);
        user.SetLNUrl(cryptoCode, false);
        var vm = await user.GetController<UIStoresController>().CheckoutAppearance().AssertViewModelAsync<CheckoutAppearanceViewModel>();
        var criteria = Assert.Single(vm.PaymentMethodCriteria);
        Assert.Equal(PaymentTypes.LN.GetPaymentMethodId(cryptoCode).ToString(), criteria.PaymentMethod);
        criteria.Value = "2 USD";
        criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.LessThan;
        Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm)
            .Result);

        var invoice = user.BitPay.CreateInvoice(
            new Invoice
            {
                Price = 1.5m,
                Currency = "USD"
            }, Facade.Merchant);
        Assert.Single(invoice.CryptoInfo);
        Assert.Equal("BTC-LN", invoice.CryptoInfo[0].PaymentType);

        // Activating LNUrl, we should still have only 1 payment criteria that can be set.
        user.RegisterLightningNode(cryptoCode);
        user.SetLNUrl(cryptoCode, true);
        vm = await user.GetController<UIStoresController>().CheckoutAppearance().AssertViewModelAsync<CheckoutAppearanceViewModel>();
        criteria = Assert.Single(vm.PaymentMethodCriteria);
        Assert.Equal(PaymentTypes.LN.GetPaymentMethodId(cryptoCode).ToString(), criteria.PaymentMethod);
        Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm).Result);

        // However, creating an invoice should show LNURL
        invoice = user.BitPay.CreateInvoice(
            new Invoice
            {
                Price = 1.5m,
                Currency = "USD"
            }, Facade.Merchant);
        Assert.Equal(2, invoice.CryptoInfo.Length);

        // Make sure this throw: Since BOLT11 and LN Url share the same criteria, there should be no payment method available
        Assert.Throws<BitPayException>(() => user.BitPay.CreateInvoice(
            new Invoice
            {
                Price = 2.5m,
                Currency = "USD"
            }, Facade.Merchant));
    }

    [Trait("Integration", "Integration")]
    [Fact]
    public async Task CanDoLightningInternalNodeMigration()
    {
        using var tester = CreateServerTester(newDb: true);
        tester.ActivateLightning(LightningConnectionType.CLightning);
        await tester.StartAsync();
        var acc = tester.NewAccount();
        await acc.GrantAccessAsync(true);
        await acc.CreateStoreAsync();

        // Test if legacy DerivationStrategy column is converted to DerivationStrategies
        var store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
        var xpub = "tpubDDmH1briYfZcTDMEc7uMEA5hinzjUTzR9yMC1drxTMeiWyw1VyCqTuzBke6df2sqbfw9QG6wbgTLF5yLjcXsZNaXvJMZLwNEwyvmiFWcLav";
        var derivation = $"{xpub}-[legacy]";
        Assert.NotNull(store);
#pragma warning disable CS0618 // Type or member is obsolete
        store.DerivationStrategy = derivation;

        await tester.PayTester.StoreRepository.UpdateStore(store);
        await tester.RestartMigration();
        store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
        Assert.NotNull(store);
        Assert.True(string.IsNullOrEmpty(store.DerivationStrategy));
        var handlers = tester.PayTester.GetService<PaymentMethodHandlerDictionary>();
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
        var v = store.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
        Assert.NotNull(v);
        Assert.Equal(derivation, v.AccountDerivation.ToString());
        Assert.Equal(derivation, v.AccountOriginal);
        Assert.Equal(xpub, v.GetFirstAccountKeySettings().AccountKey.ToString());

        await acc.RegisterLightningNodeAsync("BTC", LightningConnectionType.CLightning);
        store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);

        pmi = PaymentTypes.LN.GetPaymentMethodId("BTC");
        Assert.NotNull(store);
        var lnMethod = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, handlers);
        Assert.NotNull(lnMethod?.GetExternalLightningUrl());
        var conf = store.GetPaymentMethodConfig(pmi);
        Assert.NotNull(conf);
        conf["LightningConnectionString"] = conf["connectionString"]!.Value<string>();
        conf["DisableBOLT11PaymentOption"] = true;
        ((JObject)conf).Remove("connectionString");
        store.SetPaymentMethodConfig(pmi, conf);
        await tester.PayTester.StoreRepository.UpdateStore(store);
        await tester.RestartMigration();

        store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
        Assert.NotNull(store);
        lnMethod = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, handlers);
        Assert.Null(lnMethod?.GetExternalLightningUrl());
        Assert.True(lnMethod?.IsInternalNode);
        conf = store.GetPaymentMethodConfig(pmi);
        Assert.NotNull(conf);
        Assert.Null(conf["CryptoCode"]); // Osolete
        Assert.Null(conf["connectionString"]); // Null, so should be stripped
        Assert.Null(conf["DisableBOLT11PaymentOption"]); // Old garbage cleaned

        // Test if legacy lightning charge settings are converted to LightningConnectionString
        store.DerivationStrategies = new JObject()
        {
            new JProperty("BTC_LightningLike", new JObject()
            {
                new JProperty("LightningChargeUrl", "http://mycharge.com/"),
                new JProperty("Username", "usr"),
                new JProperty("Password", "pass"),
                new JProperty("CryptoCode", "BTC"),
                new JProperty("PaymentId", "someshit"),
            })
        }.ToString();
        await tester.PayTester.StoreRepository.UpdateStore(store);
        await tester.RestartMigration();
        store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
        Assert.NotNull(store);
        lnMethod = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, handlers);
        Assert.NotNull(lnMethod?.GetExternalLightningUrl());

        var url = lnMethod.GetExternalLightningUrl();
        LightningConnectionStringHelper.ExtractValues(url, out var connType);
        Assert.Equal(LightningConnectionType.Charge, connType);
        var client = Assert.IsType<ChargeClient>(tester.PayTester.GetService<LightningClientFactoryService>()
            .Create(url, tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC")));
        var auth = Assert.IsType<ChargeAuthentication.UserPasswordAuthentication>(client.ChargeAuthentication);

        Assert.Equal("pass", auth.NetworkCredential.Password);
        Assert.Equal("usr", auth.NetworkCredential.UserName);

        // Test if lightning connection strings get migrated to internal
        store.DerivationStrategies = new JObject()
        {
            new JProperty("BTC_LightningLike", new JObject()
            {
                new JProperty("CryptoCode", "BTC"),
                new JProperty("LightningConnectionString", tester.PayTester.IntegratedLightning),
            })
        }.ToString();
        await tester.PayTester.StoreRepository.UpdateStore(store);
        await tester.RestartMigration();
        store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
        Assert.NotNull(store);
        lnMethod = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, handlers);
        Assert.True(lnMethod?.IsInternalNode);

        store.SetPaymentMethodConfig(PaymentMethodId.Parse("BTC-LNURL"),
            new JObject()
            {
                ["CryptoCode"] = "BTC",
                ["LUD12Enabled"] = true,
                ["UseBech32Scheme"] = false,
            });
        await tester.PayTester.StoreRepository.UpdateStore(store);
        await tester.RestartMigration();
        store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
        Assert.NotNull(store);
        conf = store.GetPaymentMethodConfig(PaymentMethodId.Parse("BTC-LNURL"));
        Assert.NotNull(conf);
        Assert.Null(conf["CryptoCode"]);
        Assert.True(conf["lud12Enabled"]?.Value<bool>());
        Assert.Null(conf["useBech32Scheme"]); // default stripped
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
