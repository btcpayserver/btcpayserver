namespace BTCPayServer.Client.Models
{
    public class StoreData : StoreBaseData
    {
        /// <summary>
        /// the id of the store
        /// </summary>
        public string Id { get; set; }
    }

    public class StoreUserData
    {
        /// <summary>
        /// the id of the user
        /// </summary>
        public string UserId { get; set; }

        public string Role { get; set; }
    }
}
