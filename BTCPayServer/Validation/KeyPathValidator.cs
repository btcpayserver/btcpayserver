using System.ComponentModel.DataAnnotations;
using NBitcoin;

namespace BTCPayServer.Validation
{
    public class KeyPathValidator : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var str = value as string;
            if (string.IsNullOrWhiteSpace(str))
            {
                return ValidationResult.Success;
            }
            if (KeyPath.TryParse(str, out _))
            {
                return ValidationResult.Success;
            }
            else
            {
                return new ValidationResult("Invalid keypath");
            }
        }
    }
}
