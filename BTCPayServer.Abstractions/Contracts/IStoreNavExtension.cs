namespace BTCPayServer.Contracts
{
    public interface INavExtension
    {
        string Partial { get; }
        
        string Location { get; }
    }

    public class NavExtension: INavExtension
    {
        public NavExtension(string partial, string location)
        {
            Partial = partial;
            Location = location;
        }

        public string Partial { get; }
        public string Location { get; }
    }
}
