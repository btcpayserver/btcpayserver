namespace BTCPayServer.Payments
{
    /// <summary>
    /// <see cref="ModifyCheckoutModel"/> will always run when showing the checkout page
    /// </summary>
    public interface IGlobalCheckoutModelExtension
    {
        void ModifyCheckoutModel(CheckoutModelContext context);
    }
}
