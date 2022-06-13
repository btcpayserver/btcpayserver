namespace BTCPayServer.Events
{
    public class SettingsChanged<T>
    {
        public string SettingsName { get; set; }
        public T Settings { get; set; }
        public string StoreId { get; set; }

        public override string ToString()
        {
            return $"Settings {typeof(T).Name} changed";
        }
    }
}
