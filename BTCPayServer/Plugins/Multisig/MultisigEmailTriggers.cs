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

    public static IEnumerable<EmailTriggerViewModel> GetViewModels()
    {
        var signerPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>
        {
            new("{CryptoCode}", "The wallet crypto code"),
            new("{Request.Id}", "The multisig setup request id"),
            new("{Signer.Email}", "The signer email address"),
            new("{Signer.Name}", "The signer name"),
            new("{Signer.Link}", "The multisig setup session link")
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = SignerKeyRequested,
            Description = "Multisig - Signer Key Requested",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Signer.Email}"],
                Subject = "Multisig signer request for {CryptoCode}",
                Body = EmailsPlugin.CreateEmail(
                    "You have been invited to participate in a multisig wallet setup for {CryptoCode}. Submit your signer key to continue the setup.",
                    "Submit signer key",
                    "{Signer.Link}")
            },
            PlaceHolders = signerPlaceholders
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = SignerKeySubmitted,
            Description = "Multisig - Signer Key Submitted",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.Email}"],
                Subject = "Multisig signer submitted ({CryptoCode})",
                Body = EmailsPlugin.CreateEmail(
                    "<b>{Signer.Name}</b> submitted a signer key for the multisig wallet setup.",
                    "Open request",
                    "{Request.Link}")
            },
            PlaceHolders = new List<EmailTriggerViewModel.PlaceHolder>
            {
                new("{CryptoCode}", "The wallet crypto code"),
                new("{Request.Id}", "The multisig setup request id"),
                new("{Request.Link}", "The multisig setup link"),
                new("{Signer.Email}", "The signer email address"),
                new("{Signer.Name}", "The signer name"),
                new("{Recipient.Email}", "The recipient email address")
            }
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = WalletCreated,
            Description = "Multisig - Wallet Created",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.Email}"],
                Subject = "Multisig wallet created for {CryptoCode}",
                Body = EmailsPlugin.CreateEmail(
                    "The multisig wallet setup for {CryptoCode} is complete.",
                    "Open wallet",
                    "{Wallet.Link}")
            },
            PlaceHolders = new List<EmailTriggerViewModel.PlaceHolder>
            {
                new("{CryptoCode}", "The wallet crypto code"),
                new("{Request.Id}", "The multisig setup request id"),
                new("{Wallet.Link}", "The wallet link"),
                new("{Recipient.Email}", "The recipient email address")
            }
        };

        var pendingPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>
        {
            new("{CryptoCode}", "The wallet crypto code"),
            new("{PendingTransaction.Link}", "The pending transaction link"),
            new("{PendingTransaction.SignaturesCollected}", "The number of collected signatures"),
            new("{PendingTransaction.SignaturesNeeded}", "The number of required signatures"),
            new("{PendingTransaction.SignaturesMissing}", "The number of missing signatures"),
            new("{Recipient.Email}", "The recipient email address")
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = PendingTransactionCreated,
            Description = "Multisig - Pending Transaction Created",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{Recipient.Email}"],
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
                To = ["{Recipient.Email}"],
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
