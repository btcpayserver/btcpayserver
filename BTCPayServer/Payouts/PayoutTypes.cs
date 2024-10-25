using BTCPayServer.Payments;

namespace BTCPayServer.Payouts
{
    public class PayoutMethodIds
    {
        public static readonly PayoutMethodId TopUp = PayoutMethodId.Parse("TOPUP");
    }
    public class PayoutTypes
    {
        public static readonly PayoutType LN = new("LN");
        public static readonly PayoutType CHAIN = new("CHAIN");
    }
    public record PayoutType(string Id)
    {
        public PayoutMethodId GetPayoutMethodId(string cryptoCode) => PayoutMethodId.Parse($"{cryptoCode.ToUpperInvariant()}-{Id}");
        public override string ToString()
        {
            return Id;
        }
    }
}
