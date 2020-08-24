namespace BTCPayServer.Events
{
    public class EmailChangedEvent
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public string OldEmail { get; set; }
    }
}
