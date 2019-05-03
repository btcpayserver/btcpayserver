using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;

namespace BTCPayServer.Services.U2F.Models
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
