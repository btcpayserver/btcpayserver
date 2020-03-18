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

    public class CreateApplicationUserRequest
    {
        /// <summary>
        /// the email AND username of the new user
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// password of the new user
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// Whether this user is an administrator. If left null and there are no admins in the system, the user will be created as an admin.
        /// </summary>
        public bool? IsAdministrator { get; set; }
    }
}
