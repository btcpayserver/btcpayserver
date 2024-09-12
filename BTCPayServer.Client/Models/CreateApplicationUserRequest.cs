namespace BTCPayServer.Client.Models
{
    public class CreateApplicationUserRequest
    {
        /// <summary>
        /// the name of the new user
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// the image url of the new user
        /// </summary>
        public string ImageUrl { get; set; }

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
