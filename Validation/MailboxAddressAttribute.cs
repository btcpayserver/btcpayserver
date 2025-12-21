using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Validation
{
    /// <summary>
    /// Validate address in the format "Firstname Lastname <blah@example.com>" See rfc822
    /// </summary>
    public class MailboxAddressAttribute : ValidationAttribute
    {
        public MailboxAddressAttribute()
        {
            ErrorMessage = ErrorMessageConst;
        }
        public const string ErrorMessageConst = "Invalid mailbox address. Some valid examples are: 'test@example.com' or 'Firstname Lastname <test@example.com>'";
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value is null)
                return ValidationResult.Success;
            var str = value as string;
            if (MailboxAddressValidator.IsMailboxAddress(str))
                return ValidationResult.Success;
            return new ValidationResult(ErrorMessage);
        }
    }
}
