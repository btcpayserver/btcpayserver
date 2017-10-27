using NBitcoin;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BTCPayServer.Validations
{
    public class DerivationStrategyValidatorAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return ValidationResult.Success;
            }
            var network = (Network)validationContext.GetService(typeof(Network));
            if (network == null)
                return new ValidationResult("No Network specified");
            try
            {
                new DerivationStrategyFactory(network).Parse((string)value);
                return ValidationResult.Success;
            }
            catch (Exception ex)
            {
                return new ValidationResult(ex.Message);
            }
        }
    }
}
