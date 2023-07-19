namespace BTCPayServer.Services.Invoices
{
    public class Amounts
    {
        public string Currency { get; set; }
        /// <summary>
        /// An amount with fee included
        /// </summary>
        public decimal Gross { get; set; }
        /// <summary>
        /// An amount without fee included
        /// </summary>
        public decimal Net { get; set; }
        public override string ToString()
        {
            return $"{Currency}: Net={Net}, Gross={Gross}";
        }
    }
}
