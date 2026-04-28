namespace BTCPayServer.Client.Models
{
    public class AdminUpdateApplicationUserRequest
    {
        /// <summary>
        /// the name of the user
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// the image url of the user
        /// </summary>
        public string ImageUrl { get; set; }

        /// <summary>
        /// per-user override for the max number of stores this user can create. Null resets to the server default.
        /// </summary>
        public int? StoreQuota { get; set; }
    }
}
