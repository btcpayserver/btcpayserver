namespace BTCPayServer.Contracts
{
    public interface IUIExtension
    {
        string Partial { get; }
        
        string Location { get; }
    }

    public class UIExtension: IUIExtension
    {
        public UIExtension(string partial, string location)
        {
            Partial = partial;
            Location = location;
        }

        public string Partial { get; }
        public string Location { get; }
    }
}
