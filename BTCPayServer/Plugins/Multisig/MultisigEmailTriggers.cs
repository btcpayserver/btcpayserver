using System.Collections.Generic;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Views;

namespace BTCPayServer.Plugins.Multisig;

public static class MultisigEmailTriggers
{
    public const string SignerKeyRequested = "MultisigSignerKeyRequested";
    public const string SignerKeySubmitted = "MultisigSignerKeySubmitted";
    public const string WalletCreated = "MultisigWalletCreated";
    public const string PendingTransactionCreated = "MultisigPendingTransactionCreated";
    public const string PendingTransactionSignatureCollected = "MultisigPendingTransactionSignatureCollected";

    public static readonly string[] DefaultRuleTriggers =
    [
        SignerKeyRequested,
        SignerKeySubmitted,
        WalletCreated,
        PendingTransactionCreated,
        PendingTransactionSignatureCollected
    ];

    static List<EmailTriggerViewModel.PlaceHolder> AddSetupPlaceholders(this List<EmailTriggerViewModel.PlaceHolder> placeholders)
    {
        placeholders.Add(new("{CryptoCode}", "The wallet crypto code"));
        placeholders.Add(new("{Setup.Id}", "The multisig setup request id"));
        placeholders.Add(new("{Setup.Link}", "The multisig setup session link"));
        return placeholders;
    }
    static List<EmailTriggerViewModel.PlaceHolder> AddSignersPlaceHolders(this List<EmailTriggerViewModel.PlaceHolder> placeholders)
    {
        placeholders.Add(new("{Signer.Email}", "The signer email address"));
        placeholders.Add(new("{Signer.Name}", "The signer name"));
        placeholders.Add(new("{Signer.MailboxAddress}", "The formatted mailbox address to use when sending an email to the signer. (eg. \"John Doe\" <john.doe@example.com>)"));
        return placeholders;
    }
    static List<EmailTriggerViewModel.PlaceHolder> AddRecipientPlaceholders(this List<EmailTriggerViewModel.PlaceHolder> placeholders)
    {
        placeholders.Add(new("{Recipient.Email}", "The recipient email address"));
        placeholders.Add(new("{Recipient.Name}", "The recipient name"));
        placeholders.Add(new("{Recipient.MailboxAddress}", "The formatted mailbox address to use when sending an email to the recipient. (eg. \"John Doe\" <john.doe@example.com>)"));
        return placeholders;
    }

    public static IEnumerable<EmailTriggerViewModel> GetViewModels()
    {
        var signerPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
            .AddSetupPlaceholders()
            .AddSignersPlaceHolders();

        yield return new EmailTriggerViewModel
        {
            Trigger = SignerKeyRequested,
            Description = "Multisig - Signer Key Requested",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Signer.MailboxAddress}"],
                Subject = "Multisig signer request for {CryptoCode}",
                Body = EmailsPlugin.CreateEmail(
                    "You have been invited to participate in a multisig wallet setup for {CryptoCode}. Submit your signer key to continue the setup.",
                    "Submit signer key",
                    "{Setup.Link}")
            },
            PlaceHolders = signerPlaceholders
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = SignerKeySubmitted,
            Description = "Multisig - Signer Key Submitted",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.MailboxAddress}"],
                Subject = "Multisig signer submitted ({CryptoCode})",
                Body = EmailsPlugin.CreateEmail(
                    "<b>{Signer.Name}</b> submitted a signer key for the multisig wallet setup.",
                    "Open request",
                    "{Setup.Link}")
            },
            PlaceHolders =
                new List<EmailTriggerViewModel.PlaceHolder>()
                    .AddSetupPlaceholders()
                    .AddSignersPlaceHolders()
                    .AddRecipientPlaceholders()
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = WalletCreated,
            Description = "Multisig - Wallet Created",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.MailboxAddress}"],
                Subject = "Multisig wallet created for {CryptoCode}",
                Body = EmailsPlugin.CreateEmail(
                    "The multisig wallet setup for {CryptoCode} is complete.",
                    "Open wallet",
                    "{Wallet.Link}")
            },
            PlaceHolders = new List<EmailTriggerViewModel.PlaceHolder>()
                .AddSetupPlaceholders()
                .AddRecipientPlaceholders()
        };

        var pendingPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>
        {
            new("{CryptoCode}", "The wallet crypto code"),
            new("{PendingTransaction.Link}", "The pending transaction link"),
            new("{PendingTransaction.SignaturesCollected}", "The number of collected signatures"),
            new("{PendingTransaction.SignaturesNeeded}", "The number of required signatures"),
            new("{PendingTransaction.SignaturesMissing}", "The number of missing signatures")
        }.AddRecipientPlaceholders();

        yield return new EmailTriggerViewModel
        {
            Trigger = PendingTransactionCreated,
            Description = "Multisig - Pending Transaction Created",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.MailboxAddress}"],
                Subject = "Pending multisig transaction requires signatures ({CryptoCode})",
                Body = EmailsPlugin.CreateEmail(
                    "A pending multisig transaction was created and needs signatures.",
                    "Open pending transaction",
                    "{PendingTransaction.Link}")
            },
            PlaceHolders = pendingPlaceholders
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = PendingTransactionSignatureCollected,
            Description = "Multisig - Pending Transaction Signature Collected",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.MailboxAddress}"],
                Subject = "Multisig signature collected ({CryptoCode})",
                Body = EmailsPlugin.CreateEmail(
                    "A signer submitted a signature for the pending multisig transaction.<br/>Progress: <b>{PendingTransaction.SignaturesCollected}/{PendingTransaction.SignaturesNeeded}</b> collected, {PendingTransaction.SignaturesMissing} missing.",
                    "Open pending transaction",
                    "{PendingTransaction.Link}")
            },
            PlaceHolders = pendingPlaceholders
        };
    }
}
