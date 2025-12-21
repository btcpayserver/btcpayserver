using System;
using System.ComponentModel.DataAnnotations;
using NBitcoin;

namespace BTCPayServer.Validation
{
    public class PubKeyValidatorAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return ValidationResult.Success;
            }
            try
            {
                new PubKey((string)value);
                return ValidationResult.Success;
            }
            catch (Exception ex)
            {
                return new ValidationResult(ex.Message);
            }
        }
    }
}
