using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.BoltcardBalance.ViewModels;
using BTCPayServer.Plugins.BoltcardFactory;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BoltcardBalance.Controllers
{
    [AutoValidateAntiforgeryToken]
    public class UIBoltcardBalanceController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;

        public UIBoltcardBalanceController(
            ApplicationDbContextFactory dbContextFactory,
            SettingsRepository settingsRepository,
            BTCPayServerEnvironment env,
            BTCPayNetworkJsonSerializerSettings serializerSettings)
        {
            _dbContextFactory = dbContextFactory;
            _settingsRepository = settingsRepository;
            _env = env;
            _serializerSettings = serializerSettings;
        }
        [HttpGet("boltcards/balance")]
        public async Task<IActionResult> ScanCard([FromQuery] string p = null, [FromQuery] string c = null)
        {
            if (p is null || c is null)
            {
                return View($"{BoltcardBalancePlugin.ViewsDirectory}/ScanCard.cshtml");
            }

            //return View($"{BoltcardBalancePlugin.ViewsDirectory}/BalanceView.cshtml", new BalanceViewModel()
            //{
            //    Amount = 10000m,
            //    AmountCollected = 500m,
            //    AmountDue = 10000m,
            //    Currency = "SATS",
            //    Transactions = [new() { Date = DateTimeOffset.UtcNow, Balance = -3.0m }, new() { Date = DateTimeOffset.UtcNow, Balance = -5.0m }]
            //});

            var issuerKey = await _settingsRepository.GetIssuerKey(_env);
            var boltData = issuerKey.TryDecrypt(p);
            if (boltData?.Uid is null)
                return NotFound();
            var id = issuerKey.GetId(boltData.Uid);
            var registration = await _dbContextFactory.GetBoltcardRegistration(issuerKey, boltData, true);
            if (registration is null)
                return NotFound();
            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(registration.PullPaymentId);
            if (pp is null)
                return NotFound();
            var blob = pp.GetBlob();

            var payouts = (await ctx.Payouts.GetPayoutInPeriod(pp)
                    .OrderByDescending(o => o.Date)
                    .ToListAsync())
                    .Select(o => new
                    {
                        Entity = o,
                        Blob = o.GetBlob(_serializerSettings)
                    });


            var totalPaid = payouts.Where(p => p.Entity.State != PayoutState.Cancelled).Select(p => p.Blob.Amount).Sum();


            var vm = new BalanceViewModel()
            {
                Currency = blob.Currency,
                AmountDue = blob.Limit - totalPaid
            };
            foreach (var payout in payouts)
            {
                vm.Transactions.Add(new BalanceViewModel.Transaction()
                {
                    Date = payout.Entity.Date,
                    Balance = -payout.Blob.Amount,
                    Status = payout.Entity.State
                });
            }

            return View($"{BoltcardBalancePlugin.ViewsDirectory}/BalanceView.cshtml", vm);
        }
    }
}
