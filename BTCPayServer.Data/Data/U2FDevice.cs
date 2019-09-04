using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class U2FDevice
    {
        public string Id { get; set; }

        public string Name { get; set; }

        [Required] public byte[] KeyHandle { get; set; }

        [Required] public byte[] PublicKey { get; set; }

        [Required] public byte[] AttestationCert { get; set; }

        [Required] public int Counter { get; set; }

        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
    }
}
