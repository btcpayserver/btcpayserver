namespace BTCPayServer.Events
{
    public class NewBlockEvent:NBXplorer.Models.NewBlockEvent
    {
        public override string ToString()
        {
            return $"{CryptoCode}: New block";
        }
    }
}
 
