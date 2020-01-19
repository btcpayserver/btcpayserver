using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class APIKeyData
    {
        [MaxLength(50)]
        public string Id
        {
            get;
            set;
        }

        [MaxLength(50)] public string StoreId { get; set; }

        [MaxLength(50)] public string UserId { get; set; }
        public string ApplicationIdentifier { get; set; }

        public APIKeyType Type { get; set; } = APIKeyType.Legacy;
        public string Permissions { get; set; }
        
        public StoreData StoreData { get; set; }
        public ApplicationUser User { get; set; }
        public string[] GetPermissions() { return Permissions?.Split(';') ?? new string[0]; }

        public void SetPermissions(IEnumerable<string> permissions)
        {
            Permissions = string.Join(';',
                permissions?.Select(s => s.Replace(";", string.Empty)) ?? new string[0]);
        }
    }

    public enum APIKeyType
    {
        Legacy,
        Permanent
    }
}
