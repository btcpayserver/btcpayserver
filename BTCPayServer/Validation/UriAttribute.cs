using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BTCPayServer.Validation
{
    //from https://stackoverflow.com/a/47196738/275504
    public class UriAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var str = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            bool valid = string.IsNullOrWhiteSpace(str) || Uri.TryCreate(str, UriKind.Absolute, out Uri _);

            if (!valid)
            {
                return new ValidationResult(ErrorMessage);
            }
            return ValidationResult.Success;
        }
    }
}
