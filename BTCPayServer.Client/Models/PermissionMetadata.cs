using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class PermissionMetadata
    {
        static PermissionMetadata()
        {
            Dictionary<string, PermissionMetadata> nodes = new Dictionary<string, PermissionMetadata>();
            foreach (var policy in Client.Policies.AllPolicies)
            {
                nodes.Add(policy, new PermissionMetadata() { PermissionName = policy });
            }
            foreach (var n in nodes)
            {
                foreach (var policy in Client.Policies.AllPolicies)
                {
                    if (policy.Equals(n.Key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Client.Permission.Create(n.Key).Contains(Client.Permission.Create(policy)))
                        n.Value.SubPermissions.Add(policy);
                }
            }
            foreach (var n in nodes)
            {
                n.Value.SubPermissions.Sort();
            }
            PermissionNodes = nodes.Values.OrderBy(v => v.PermissionName).ToArray();
        }
        public readonly static PermissionMetadata[] PermissionNodes;
        [JsonProperty("name")]
        public string PermissionName { get; set; }
        [JsonProperty("included")]
        public List<string> SubPermissions { get; set; } = new List<string>();
    }
}
