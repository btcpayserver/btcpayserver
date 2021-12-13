namespace BTCPayServer.Client.Models
{
    public class GreenfieldPermissionAPIError : GreenfieldAPIError
    {
        public GreenfieldPermissionAPIError(string missingPermission) : base()
        {
            MissingPermission = missingPermission;
            
            // TODO for some reason this line does not work??
            // MissingPermissionDescription = BTCPayServer.Controllers.ManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions[missingPermission].Description;
            MissingPermissionDescription = "TODO";

            Code = "insufficient-api-permissions";
            Message = $"Insufficient API Permissions. Please use an API key with permission \"{MissingPermissionDescription}\" ({missingPermission}).";
        }

        public string MissingPermission { get; }
        public string MissingPermissionDescription { get; }
        
    }
}
