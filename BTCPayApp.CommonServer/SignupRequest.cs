using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.CommonServer;

public class SignupRequest
{
    [Required]
    public string? Email { get; init; }
    
    [Required]
    public string? Password { get; init; }
}
