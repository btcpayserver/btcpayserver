namespace BTCPayServer.Payments.Changelly
{
    public static class ChangellyCalculationHelper
    {
        public static decimal ComputeBaseAmount(decimal baseRate, decimal toAmount)
        {
            return (1m / baseRate) * toAmount;
        }

        public static decimal ComputeCorrectAmount(decimal currentFromAmount, decimal currentAmount,
            decimal expectedAmount)
        {
            return (currentFromAmount / currentAmount) * expectedAmount;
        }
    }
}
