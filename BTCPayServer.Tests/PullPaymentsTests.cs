using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.NTag424;
using BTCPayServer.Views.Stores;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class PullPaymentsTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Integration", "Integration")]
    public async Task CanTopUpPullPayment()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        await user.RegisterDerivationSchemeAsync("BTC");
        var client = await user.CreateClient();
        var pp = await client.CreatePullPayment(user.StoreId, new()
        {
            Currency = "BTC",
            Amount = 1.0m,
            PayoutMethods = [ "BTC-CHAIN" ]
        });
        var controller = user.GetController<UIInvoiceController>();
        var invoice = await controller.CreateInvoiceCoreRaw(new()
        {
            Amount = 0.5m,
            Currency = "BTC",
        }, controller.HttpContext.GetStoreData(), controller.Url.Link(null, null)!, [PullPaymentHostedService.GetInternalTag(pp.Id)]);
        await client.MarkInvoiceStatus(user.StoreId, invoice.Id, new() { Status = InvoiceStatus.Settled });

        await TestUtils.EventuallyAsync(async () =>
        {
            var payouts = await client.GetPayouts(pp.Id);
            var payout = Assert.Single(payouts);
            Assert.Equal("TOPUP", payout.PayoutMethodId);
            Assert.Equal(invoice.Id, payout.Destination);
            Assert.Equal(-0.5m, payout.OriginalAmount);
        });
    }

    [Fact]
    public async Task CanMigratePayoutsAndPullPayments()
    {
        var tester = CreateDBTester();
        await tester.MigrateUntil("20240827034505_migratepayouts");

        await using var ctx = tester.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await conn.ExecuteAsync("INSERT INTO \"Stores\"(\"Id\", \"SpeedPolicy\") VALUES (@store, 0)", new { store = "store1" });
        var param = new
        {
            Id = "pp1",
            StoreId = "store1",
            Blob = "{\"Name\": \"CoinLottery\", \"View\": {\"Email\": null, \"Title\": \"\", \"Description\": \"\", \"EmbeddedCSS\": null, \"CustomCSSLink\": null}, \"Limit\": \"10.00\", \"Period\": null, \"Currency\": \"GBP\", \"Description\": \"\", \"Divisibility\": 0, \"MinimumClaim\": \"0\", \"AutoApproveClaims\": false, \"SupportedPaymentMethods\": [\"BTC\", \"BTC_LightningLike\"]}"
        };
        await conn.ExecuteAsync("INSERT INTO \"PullPayments\"(\"Id\", \"StoreId\", \"Blob\", \"StartDate\", \"Archived\") VALUES (@Id, @StoreId, @Blob::JSONB, NOW(), 'f')", param);
        var parameters = new[]
        {
            new
            {
                Id = "p1",
                StoreId = "store1",
                PullPaymentDataId = "pp1",
                PaymentMethodId = "BTC",
                Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": \"0.00012225\", \"MinimumConfirmation\": 1}"
            },
            new
            {
                Id = "p2",
                StoreId = "store1",
                PullPaymentDataId = "pp1",
                PaymentMethodId = "BTC_LightningLike",
                Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
            },
            new
            {
                Id = "p3",
                StoreId = "store1",
                PullPaymentDataId = null as string,
                PaymentMethodId = "BTC_LightningLike",
                Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
            },
            new
            {
                Id = "p4",
                StoreId = "store1",
                PullPaymentDataId = null as string,
                PaymentMethodId = "BTC_LightningLike",
                Blob = "{\"Amount\": \"-10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
            }
        };
        await conn.ExecuteAsync("INSERT INTO \"Payouts\"(\"Id\", \"StoreDataId\", \"PullPaymentDataId\", \"PaymentMethodId\", \"Blob\", \"State\", \"Date\") VALUES (@Id, @StoreId, @PullPaymentDataId, @PaymentMethodId, @Blob::JSONB, 'state', NOW())", parameters);
        await tester.CompleteMigrations();

        var migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"PullPayments\" WHERE \"Id\"='pp1' AND \"Limit\"=10.0 AND \"Currency\"='GBP' AND \"Blob\"->>'SupportedPayoutMethods'='[\"BTC-CHAIN\", \"BTC-LN\"]'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p1' AND \"Amount\"= 0.00012225 AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='GBP' AND \"PayoutMethodId\"='BTC-CHAIN'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p2' AND \"Amount\" IS NULL AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='GBP' AND \"PayoutMethodId\"='BTC-LN'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p3' AND \"Amount\" IS NULL AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='BTC'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p4' AND \"Amount\" IS NULL AND \"OriginalAmount\"=-10.0 AND \"OriginalCurrency\"='BTC' AND \"PayoutMethodId\"='TOPUP'");
        Assert.True(migrated);
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task CanUsePullPaymentViaAPI()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var acc = tester.NewAccount();
        await acc.GrantAccessAsync(true);
        acc.RegisterLightningNode("BTC", LightningConnectionType.CLightning, false);
        var storeId = (await acc.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true)).StoreId;
        var client = await acc.CreateClient();
        var result = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test",
            Description = "Test description",
            Amount = 12.3m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });

        void VerifyResult()
        {
            Assert.Equal("Test", result.Name);
            Assert.Equal("Test description", result.Description);
            // If it contains ? it means that we are resolving an unknown route with the link generator
            Assert.DoesNotContain("?", result.ViewLink);
            Assert.False(result.Archived);
            Assert.Equal("BTC", result.Currency);
            Assert.Equal(12.3m, result.Amount);
        }

        VerifyResult();

        var unauthenticated = new BTCPayServerClient(tester.PayTester.ServerUri);
        result = await unauthenticated.GetPullPayment(result.Id);
        VerifyResult();
        await AssertEx.AssertHttpError(404, async () => await unauthenticated.GetPullPayment("lol"));
        // Can't list pull payments unauthenticated
        await AssertEx.AssertHttpError(401, async () => await unauthenticated.GetPullPayments(storeId));

        var pullPayments = await client.GetPullPayments(storeId);
        result = Assert.Single(pullPayments);
        VerifyResult();

        var test2 = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 2",
            Amount = 12.3m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" },
            BOLT11Expiration = TimeSpan.FromDays(31.0)
        });
        Assert.Equal(TimeSpan.FromDays(31.0), test2.BOLT11Expiration);

        TestLogs.LogInformation("Can't archive without knowing the walletId");
        var ex = await AssertEx.AssertApiError("missing-permission", async () => await client.ArchivePullPayment("lol", result.Id));
        Assert.Equal("btcpay.store.canarchivepullpayments", ((GreenfieldPermissionAPIError)ex.APIError).MissingPermission);
        TestLogs.LogInformation("Can't archive without permission");
        await AssertEx.AssertApiError("unauthenticated", async () => await unauthenticated.ArchivePullPayment(storeId, result.Id));
        await client.ArchivePullPayment(storeId, result.Id);
        result = await unauthenticated.GetPullPayment(result.Id);
        Assert.Equal(TimeSpan.FromDays(30.0), result.BOLT11Expiration);
        Assert.True(result.Archived);
        var pps = await client.GetPullPayments(storeId);
        result = Assert.Single(pps);
        Assert.Equal("Test 2", result.Name);
        pps = await client.GetPullPayments(storeId, true);
        Assert.Equal(2, pps.Length);
        Assert.Equal("Test 2", pps[0].Name);
        Assert.Equal("Test", pps[1].Name);

        var payouts = await unauthenticated.GetPayouts(pps[0].Id);
        Assert.Empty(payouts);

        var destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        await AssertEx.AssertApiError("overdraft", async () => await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            Amount = 1_000_000m,
            PayoutMethodId = "BTC",
        }));

        await AssertEx.AssertApiError("archived", async () => await unauthenticated.CreatePayout(pps[1].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        }));

        var payout = await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });

        payouts = await unauthenticated.GetPayouts(pps[0].Id);
        var payout2 = Assert.Single(payouts);
        Assert.Equal(payout.OriginalAmount, payout2.OriginalAmount);
        Assert.Equal(payout.Id, payout2.Id);
        Assert.Equal(destination, payout2.Destination);
        Assert.Equal(PayoutState.AwaitingApproval, payout.State);
        Assert.Equal("BTC-CHAIN", payout2.PayoutMethodId);
        Assert.Equal("BTC", payout2.PayoutCurrency);
        Assert.Null(payout.PayoutAmount);

        TestLogs.LogInformation("Can't overdraft");

        var destination2 = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        await AssertEx.AssertApiError("overdraft", async () => await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination2,
            Amount = 0.00001m,
            PayoutMethodId = "BTC"
        }));

        TestLogs.LogInformation("Can't create too low payout");
        await AssertEx.AssertApiError("amount-too-low", async () => await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination2,
            PayoutMethodId = "BTC"
        }));

        TestLogs.LogInformation("Can archive payout");
        await client.CancelPayout(storeId, payout.Id);
        payouts = await unauthenticated.GetPayouts(pps[0].Id);
        Assert.Empty(payouts);

        payouts = await client.GetPayouts(pps[0].Id, true);
        payout = Assert.Single(payouts);
        Assert.Equal(PayoutState.Cancelled, payout.State);

        TestLogs.LogInformation("Can create payout after cancelling");
        await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });

        var start = TestUtils.RoundSeconds(DateTimeOffset.Now + TimeSpan.FromDays(7.0));
        var inFuture = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Starts in the future",
            Amount = 12.3m,
            StartsAt = start,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });
        Assert.Equal(start, inFuture.StartsAt);
        Assert.Null(inFuture.ExpiresAt);
        await AssertEx.AssertApiError("not-started", async () => await unauthenticated.CreatePayout(inFuture.Id, new CreatePayoutRequest()
        {
            Amount = 1.0m,
            Destination = destination,
            PayoutMethodId = "BTC"
        }));

        var expires = TestUtils.RoundSeconds(DateTimeOffset.Now - TimeSpan.FromDays(7.0));
        var inPast = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Will expires",
            Amount = 12.3m,
            ExpiresAt = expires,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });
        await AssertEx.AssertApiError("expired", async () => await unauthenticated.CreatePayout(inPast.Id, new CreatePayoutRequest()
        {
            Amount = 1.0m,
            Destination = destination,
            PayoutMethodId = "BTC"
        }));

        await AssertEx.AssertValidationError(new[] { "ExpiresAt" }, async () => await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 2",
            Amount = 12.3m,
            StartsAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(1)
        }));


        TestLogs.LogInformation("Create a pull payment with USD");
        var pp = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test USD",
            Amount = 5000m,
            Currency = "USD",
            PayoutMethods = new[] { "BTC" }
        });

        await AssertEx.AssertApiError("lnurl-not-supported", async () => await unauthenticated.GetPullPaymentLNURL(pp.Id));

        destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        TestLogs.LogInformation("Try to pay it in BTC");
        payout = await unauthenticated.CreatePayout(pp.Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });
        await AssertEx.AssertApiError("old-revision", async () => await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            Revision = -1
        }));
        await AssertEx.AssertApiError("rate-unavailable", async () => await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            RateRule = "DONOTEXIST(BTC_USD)"
        }));
        payout = await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            Revision = payout.Revision
        });
        Assert.Equal(PayoutState.AwaitingPayment, payout.State);
        Assert.NotNull(payout.PayoutAmount);
        Assert.Equal(1.0m, payout.PayoutAmount); // 1 BTC == 5000 USD in tests
        await AssertEx.AssertApiError("invalid-state", async () => await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            Revision = payout.Revision
        }));

        // Create one pull payment with an amount of 9 decimals
        var test3 = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 2",
            Amount = 12.303228134m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });
        destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        payout = await unauthenticated.CreatePayout(test3.Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });
        payout = await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest());
        // The payout should round the value of the payment down to the network of the payment method
        Assert.Equal(12.30322814m, payout.PayoutAmount);
        Assert.Equal(12.303228134m, payout.OriginalAmount);

        await client.MarkPayoutPaid(storeId, payout.Id);
        payout = (await client.GetPayouts(payout.PullPaymentId)).First(data => data.Id == payout.Id);
        Assert.Equal(PayoutState.Completed, payout.State);
        await AssertEx.AssertApiError("invalid-state", async () => await client.MarkPayoutPaid(storeId, payout.Id));

        // Test LNURL values
        var test4 = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 3",
            Amount = 12.303228134m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC", "BTC-LightningNetwork", "BTC_LightningLike" }
        });
        var lnrUrLs = await unauthenticated.GetPullPaymentLNURL(test4.Id);
        Assert.IsType<string>(lnrUrLs.LNURLBech32);
        Assert.IsType<string>(lnrUrLs.LNURLUri);
        Assert.Equal(12.303228134m, test4.Amount);
        Assert.Equal("BTC", test4.Currency);

        // Check we can register Boltcard
        var uid = new byte[7];
        RandomNumberGenerator.Fill(uid);
        var card = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid
        });
        Assert.Equal(0, card.Version);
        var card1Keys = new[] { card.K0, card.K1, card.K2, card.K3, card.K4 };
        Assert.DoesNotContain(null, card1Keys);

        var card2 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid
        });
        Assert.Equal(0, card2.Version);
        card2 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid,
            OnExisting = OnExistingBehavior.UpdateVersion
        });
        Assert.Equal(1, card2.Version);
        Assert.StartsWith("lnurlw://", card2.LNURLW);
        Assert.EndsWith("/boltcard", card2.LNURLW);
        var card2Keys = new[] { card2.K0, card2.K1, card2.K2, card2.K3, card2.K4 };
        Assert.DoesNotContain(null, card2Keys);
        for (var i = 0; i < card1Keys.Length; i++)
        {
            if (i == 1)
                Assert.Contains(card1Keys[i], card2Keys);
            else
                Assert.DoesNotContain(card1Keys[i], card2Keys);
        }

        var card3 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid,
            OnExisting = OnExistingBehavior.KeepVersion
        });
        Assert.Equal(card2.Version, card3.Version);
        var p = new byte[] { 0xc7 }.Concat(uid).Concat(new byte[8]).ToArray();
        var card4 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            LNURLW = card2.LNURLW + $"?p={Encoders.Hex.EncodeData(AESKey.Parse(card2.K1).Encrypt(p))}"
        });
        Assert.Equal(card2.Version, card4.Version);
        Assert.Equal(card2.K4, card4.K4);
        // Can't define both properties
        await AssertEx.AssertValidationError(["LNURLW"], () => client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            UID = uid,
            LNURLW = card2.LNURLW + $"?p={Encoders.Hex.EncodeData(AESKey.Parse(card2.K1).Encrypt(p))}"
        }));
        // p is malformed
        await AssertEx.AssertValidationError(["LNURLW"], () => client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            UID = uid,
            LNURLW = card2.LNURLW + $"?p=lol"
        }));
        // p is invalid
        p[0] = 0;
        await AssertEx.AssertValidationError(["LNURLW"], () => client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            LNURLW = card2.LNURLW + $"?p={Encoders.Hex.EncodeData(AESKey.Parse(card2.K1).Encrypt(p))}"
        }));
        // Test with SATS denomination values
        var testSats = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test SATS",
            Amount = 21000,
            Currency = "SATS",
            PayoutMethods = new[] { "BTC", "BTC-LightningNetwork", "BTC_LightningLike" }
        });
        lnrUrLs = await unauthenticated.GetPullPaymentLNURL(testSats.Id);
        Assert.IsType<string>(lnrUrLs.LNURLBech32);
        Assert.IsType<string>(lnrUrLs.LNURLUri);
        Assert.Equal(21000, testSats.Amount);
        Assert.Equal("SATS", testSats.Currency);

        //permission test around auto approved pps and payouts
        var nonApproved = await acc.CreateClient(Policies.CanCreateNonApprovedPullPayments);
        var approved = await acc.CreateClient(Policies.CanCreatePullPayments);
        await AssertEx.AssertPermissionError(Policies.CanCreatePullPayments, async () =>
        {
            await nonApproved.CreatePullPayment(acc.StoreId, new CreatePullPaymentRequest()
            {
                Amount = 100,
                Currency = "USD",
                Name = "pull payment",
                PayoutMethods = new[] { "BTC" },
                AutoApproveClaims = true
            });
        });
        await AssertEx.AssertPermissionError(Policies.CanCreatePullPayments, async () =>
        {
            await nonApproved.CreatePayout(acc.StoreId, new CreatePayoutThroughStoreRequest()
            {
                Amount = 100,
                PayoutMethodId = "BTC",
                Approved = true,
                Destination = new Key().GetAddress(ScriptPubKeyType.TaprootBIP86, Network.RegTest).ToString()
            });
        });

        await approved.CreatePullPayment(acc.StoreId, new CreatePullPaymentRequest()
        {
            Amount = 100,
            Currency = "USD",
            Name = "pull payment",
            PayoutMethods = new[] { "BTC" },
            AutoApproveClaims = true
        });

        await approved.CreatePayout(acc.StoreId, new CreatePayoutThroughStoreRequest()
        {
            Amount = 100,
            PayoutMethodId = "BTC",
            Approved = true,
            Destination = new Key().GetAddress(ScriptPubKeyType.TaprootBIP86, Network.RegTest).ToString()
        });
    }

    [Fact]
    [Trait("Playwright", "Playwright-2")]
    public async Task CanEditPullPaymentUI()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.GenerateWallet("BTC", "", true, true);
        await s.Server.ExplorerNode.GenerateAsync(1);
        await s.FundStoreWallet(denomination: 50.0m);

        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP1");
        await s.Page.Locator("#Amount").ClearAsync();
        await s.Page.FillAsync("#Amount", "99.0");
        await s.ClickPagePrimary();

        var opening = s.Page.Context.WaitForPageAsync();
        await s.Page.ClickAsync("text=View");
        var newPage = await opening;
        await Expect(newPage.Locator("body")).ToContainTextAsync("PP1");
        await newPage.CloseAsync();

        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

        await s.Page.ClickAsync("text=PP1");
        await s.Page.FillAsync(".note-editable", "Description Edit");
        await s.Page.FillAsync("#Name", "PP1 Edited");
        await s.ClickPagePrimary();

        await s.FindAlertMessage();

        opening = s.Page.Context.WaitForPageAsync();
        await s.Page.ClickAsync("text=View");
        await using (await s.SwitchPage(opening))
        {
            try
            {
                await Expect(s.Page.GetByTestId("description")).ToContainTextAsync("Description Edit");
                await Expect(s.Page.GetByTestId("title")).ToContainTextAsync("PP1 Edited");
            }
            catch
            {
                await s.TakeScreenshot("Flaky-CanEditPullPaymentUI.png");
                throw;
            }
        }
    }
}
