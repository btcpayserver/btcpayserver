#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MimeKit;

namespace BTCPayServer
{
    /// <summary>
    /// Validate address in the format "Firstname Lastname <blah@example.com>" See rfc5322
    /// </summary>
    public class MailboxAddressValidator
    {
        static ParserOptions _options;
        static MailboxAddressValidator()
        {
            _options = ParserOptions.Default.Clone();
            _options.AllowAddressesWithoutDomain = false;
        }
        public static bool IsMailboxAddress(string? str)
        {
            return TryParse(str, out _);
        }
        public static MailboxAddress Parse(string? str)
        {
            if (!TryParse(str, out var mb))
                throw new FormatException("Invalid mailbox address (rfc5322)");
            return mb;
        }
        public static bool TryParse(string? str, [MaybeNullWhen(false)] out MailboxAddress mailboxAddress)
        {
            mailboxAddress = null;
            if (String.IsNullOrWhiteSpace(str))
                return false;
            return MailboxAddress.TryParse(_options, str, out mailboxAddress) && mailboxAddress is not null;
        }
    }
}
