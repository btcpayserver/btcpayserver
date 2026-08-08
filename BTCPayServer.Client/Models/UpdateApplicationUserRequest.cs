namespace BTCPayServer.Client.Models;

public class UpdateApplicationUserRequest
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
    /// the email AND username of the user
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// current password of the user, required to change the email or the password
    /// </summary>
    public string CurrentPassword { get; set; }

    /// <summary>
    /// new password of the user
    /// </summary>
    public string NewPassword { get; set; }

    /// <summary>
    /// whether to allow Basic authentication for the Greenfield API
    /// </summary>
    public bool? AllowGreenfieldBasicAuth { get; set; }
}
