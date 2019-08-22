namespace BTCPayServer.Events
{
    public class SettingsChanged<T>
    {
        public T Settings { get; set; }
    }
}