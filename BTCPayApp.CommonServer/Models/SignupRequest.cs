using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.CommonServer.Models;

public class SignupRequest
{
    [Required]
    public string Email { get; init; } = null!;
    
    [Required]
    public string Password { get; init; } = null!;
}
