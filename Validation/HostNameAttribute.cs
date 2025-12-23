using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BTCPayServer.Validation
{
    //from http://stackoverflow.com/questions/967516/ddg#967610
    public class HostNameAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var str = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            var valid = string.IsNullOrWhiteSpace(str) || Uri.CheckHostName(str) != UriHostNameType.Unknown;

            if (!valid)
            {
                return new ValidationResult(ErrorMessage);
            }

            return ValidationResult.Success;
        }
    }
}
