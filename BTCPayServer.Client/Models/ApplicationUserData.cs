namespace BTCPayServer.Client.Models
{
    public class ApplicationUserData
    {
        /// <summary>
        /// the id of the user
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// the email AND username of the user
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Whether the user has verified their email
        /// </summary>
        public bool EmailConfirmed { get; set; }

        /// <summary>
        /// whether the user needed to verify their email on account creation
        /// </summary>
        public bool RequiresEmailConfirmation { get; set; }
    }
}
