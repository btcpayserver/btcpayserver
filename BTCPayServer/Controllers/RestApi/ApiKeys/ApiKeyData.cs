using BTCPayServer.Data;

namespace BTCPayServer.Controllers.RestApi.ApiKeys
{
    public class ApiKeyData
    {
        public string ApiKey { get; set; }
        public string Label { get; set; }
        public string UserId { get; set; }
        public string[] Permissions  { get; set; }

        public static ApiKeyData FromModel(APIKeyData data)
        {
            return new ApiKeyData()
            {
                Permissions = data.GetPermissions(),
                ApiKey = data.Id,
                UserId = data.UserId,
                Label = data.Label
            };
        }
    }
}
