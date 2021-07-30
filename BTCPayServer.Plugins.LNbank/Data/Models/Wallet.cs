using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using BTCPayServer.Lightning;

namespace BTCPayServer.Plugins.LNbank.Data.Models
{
    public class Wallet
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [DisplayName("Wallet ID")]
        public string WalletId { get; set; }
        [DisplayName("User ID")]
        public string UserId { get; set; }
        [Required]
        public string Name { get; set; }
        [DisplayName("Creation date")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<Transaction> Transactions { get; set; } = new List<Transaction>();

        public LightMoney Balance
        {
            get
            {
                return Transactions
                    .Where(t => t.AmountSettled != null)
                    .Aggregate(new LightMoney(0), (total, t) => total + t.AmountSettled);
            }
        }
    }
}
