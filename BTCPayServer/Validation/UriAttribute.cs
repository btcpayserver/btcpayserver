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
            _allowedSchemes = allowedSchemes.Length is 0
                ? [Uri.UriSchemeHttp, Uri.UriSchemeHttps]
                : allowedSchemes;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var str = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            bool valid = string.IsNullOrEmpty(str) ||
                         Uri.TryCreate(str, UriKind.Absolute, out var uri) &&
                          Array.Exists(_allowedSchemes,
                              scheme => scheme.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase));

            if (!valid)
            {
                return new ValidationResult(ErrorMessage);
            }
            return ValidationResult.Success;
        }
    }
}
