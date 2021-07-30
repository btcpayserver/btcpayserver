using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.LNbank.Pages.Wallets
{
    public class ReceiveModel : BasePageModel
    {
        public Wallet Wallet { get; set; }
        [BindProperty]
        public string Description { get; set; }
        [BindProperty]
        [DisplayName("Amount in sats")]
        [Required]
        public long Amount { get; set; }
        public string ErrorMessage { get; set; }

        public ReceiveModel(
            UserManager<ApplicationUser> userManager, 
            WalletService walletService) : base(userManager, walletService) {}
        
        public async Task<IActionResult> OnGet(string walletId)
        {
            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId,
                IncludeTransactions = true
            });

            if (Wallet == null) return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string walletId)
        {
            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId,
                IncludeTransactions = true
            });

            if (Wallet == null) return NotFound();
            if (!ModelState.IsValid) return Page();

            try
            {
                var amount = LightMoney.Satoshis(Amount).MilliSatoshi;
                var transaction = await WalletService.Receive(Wallet, amount, Description);
                var transactionId = transaction.TransactionId;
                return RedirectToPage("/Transactions/Details", new { walletId, transactionId });
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
            }

            return Page();
        }
    }
}
