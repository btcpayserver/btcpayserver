using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.NTag424;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using ExchangeSharp;
using LNURL;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class ChromeTests : UnitTestBase
    {
        private const int TestTimeout = TestUtils.TestTimeout;

        public ChromeTests(ITestOutputHelper helper) : base(helper)
        {
        }

        private string RandomBytes(int count)
        {
            var c = RandomNumberGenerator.GetBytes(count);
            return Encoders.Hex.EncodeData(c);
        }

        // For god know why, selenium have problems clicking on the save button, resulting in ultimate hacks
        // to make it works.
        private void SudoForceSaveLightningSettingsRightNowAndFast(SeleniumTester s, string cryptoCode)
        {
            int maxAttempts = 5;
retry:
            s.ClickPagePrimary();
            try
            {
                Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);
            }
            catch (NoSuchElementException) when (maxAttempts > 0)
            {
                maxAttempts--;
                goto retry;
            }
        }

        private static string AssertUrlHasPairingCode(SeleniumTester s)
        {
            var regex = Regex.Match(new Uri(s.Driver.Url, UriKind.Absolute).Query, "pairingCode=([^&]*)");
            Assert.True(regex.Success, $"{s.Driver.Url} does not match expected regex");
            var pairingCode = regex.Groups[1].Value;
            return pairingCode;
        }

        private void SetTransactionOutput(SeleniumTester s, int index, BitcoinAddress dest, decimal amount, bool subtract = false)
        {
            s.Driver.FindElement(By.Id($"Outputs_{index}__DestinationAddress")).SendKeys(dest.ToString());
            var amountElement = s.Driver.FindElement(By.Id($"Outputs_{index}__Amount"));
            amountElement.Clear();
            amountElement.SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var checkboxElement = s.Driver.FindElement(By.Id($"Outputs_{index}__SubtractFeesFromOutput"));
            if (checkboxElement.Selected != subtract)
            {
                checkboxElement.Click();
            }
        }
    }
}
