namespace BTCPayServer.Events
{
    public class SettingsChanged<T>
    {
        public string SettingsName { get; set; }
        public string StoreId { get; set; }
        public T Settings { get; set; }
        public override string ToString()
        {
            return $"Settings {SettingsName} {(string.IsNullOrEmpty(StoreId)? string.Empty : $"for store {StoreId} ")}changed";
        }
    }
}
