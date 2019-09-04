namespace BTCPayServer.Events
{
    public class SettingsChanged<T>
    {
        public T Settings { get; set; }
        public override string ToString()
        {
            return Settings?.ToString() ?? string.Empty;
        }
    }
}
