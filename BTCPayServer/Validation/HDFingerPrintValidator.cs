using System.ComponentModel.DataAnnotations;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Validation
{
    public class HDFingerPrintValidator : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var str = value as string;
            if (string.IsNullOrWhiteSpace(str))
            {
                return ValidationResult.Success;
            }

            try
            {
                new HDFingerprint(Encoders.Hex.DecodeData(str));
                return ValidationResult.Success;
            }
            catch
            {
                return new ValidationResult("Invalid fingerprint");
            }
        }
    }
}
