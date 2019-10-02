namespace BTCPayServer.U2F.Models
{
    public class AddU2FDeviceViewModel
    {
        public string AppId{ get; set; }
        public string Challenge { get; set; }
        public string Version { get; set; }
        public string DeviceResponse { get; set; }

        public string Name { get; set; }
    }
}
