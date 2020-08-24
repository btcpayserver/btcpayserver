using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Zammad
{
    public class ZammadOptionsVM : ZammadOptions
    {
        public SelectList Groups { get; set; }
        public SelectList Organizations { get; set; }

        public void FromOptions(ZammadOptions options)
        {
            if(options is null){
                return;
            }
            this.Configured = options.Configured;
            this.Endpoint = options.Endpoint;
            this.APIKey = options.APIKey;
            this.ServerTicketsGroupId = options.ServerTicketsGroupId;
            this.ServerUserOrganizationId = options.ServerUserOrganizationId;
            this.Enabled = options.Enabled;
        }
        public ZammadOptions ToOptions()
        {
            return new ZammadOptions()
            {
                Configured = Configured,
                Endpoint = Endpoint,
                APIKey = APIKey,
                ServerTicketsGroupId = ServerTicketsGroupId,
                ServerUserOrganizationId = ServerUserOrganizationId,
                Enabled = Enabled
            };
        }
    }
}
