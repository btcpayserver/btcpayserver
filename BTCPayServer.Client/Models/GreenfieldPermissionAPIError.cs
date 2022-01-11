using System;

namespace BTCPayServer.Client.Models
{
    public class GreenfieldPermissionAPIError : GreenfieldAPIError
    {
        public GreenfieldPermissionAPIError(string missingPermission, string message = null) : base()
        {
            MissingPermission = missingPermission;
            Code = "missing-permission";
            Message = message ?? $"Insufficient API Permissions. Please use an API key with permission \"{MissingPermission}\". You can create an API key in your account's settings / Api Keys.";
        }

        public string MissingPermission { get; }

    }
}
