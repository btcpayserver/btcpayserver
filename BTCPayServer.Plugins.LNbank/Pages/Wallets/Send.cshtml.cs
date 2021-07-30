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

namespace BTCPayServer.Plugins.LNbank.Pages.Wallets
{
    public class SendModel : BasePageModel
    {
        public Wallet Wallet { get; set; }
        public BOLT11PaymentRequest Bolt11 { get; set; }
        [BindProperty]
        [DisplayName("Payment Request")]
        [Required]
        public string PaymentRequest { get; set; }
        public string ErrorMessage { get; set; }

        public SendModel(
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

        public async Task<IActionResult> OnPostDecodeAsync(string walletId)
        {
            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId,
                IncludeTransactions = true
            });

            if (Wallet == null) return NotFound();
            if (!ModelState.IsValid) return Page();

            Bolt11 = WalletService.ParsePaymentRequest(PaymentRequest);

            return Page();
        }

        public async Task<IActionResult> OnPostConfirmAsync(string walletId)
        {
            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId,
                IncludeTransactions = true
            });

            if (Wallet == null) return NotFound();
            if (!ModelState.IsValid) return Page();

            Bolt11 = WalletService.ParsePaymentRequest(PaymentRequest);

            try
            {
                await WalletService.Send(Wallet, Bolt11, PaymentRequest);
                return RedirectToPage("./Index", new { walletId });
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
            }

            return Page();
        }
    }
}
