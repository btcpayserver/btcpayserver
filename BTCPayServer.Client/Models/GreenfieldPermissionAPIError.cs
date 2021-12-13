using System;

namespace BTCPayServer.Client.Models
{
    public class GreenfieldPermissionAPIError : GreenfieldAPIError
    {
        public GreenfieldPermissionAPIError(string missingPermission, string missingPermissionDescription) : base()
        {
            MissingPermission = missingPermission;
            MissingPermissionDescription = missingPermissionDescription;
            Code = "insufficient-api-permissions";
            Message = $"Insufficient API Permissions. Please use an API key with permission \"{MissingPermissionDescription}\" ({missingPermission}).";
        }

        public string MissingPermission { get; }
        public string MissingPermissionDescription { get; }
        
    }
}
