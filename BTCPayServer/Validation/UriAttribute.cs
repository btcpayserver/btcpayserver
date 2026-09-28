using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BTCPayServer.Validation
{
    //from https://stackoverflow.com/a/47196738/275504
    public class UriAttribute : ValidationAttribute
    {
        private readonly string[] _allowedSchemes;

        public UriAttribute(params string[] allowedSchemes)
        {
            _allowedSchemes = allowedSchemes;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var str = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            bool valid = string.IsNullOrWhiteSpace(str) ||
                         Uri.TryCreate(str, UriKind.Absolute, out var uri) &&
                         (_allowedSchemes.Length == 0 || Array.Exists(_allowedSchemes,
                             scheme => scheme.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase)));

            if (!valid)
            {
                return new ValidationResult(ErrorMessage);
            }
            return ValidationResult.Success;
        }
    }
}
