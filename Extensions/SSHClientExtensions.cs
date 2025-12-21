using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.SSH;
using Renci.SshNet;

namespace BTCPayServer
{
    public static class SSHClientExtensions
    {
        public static async Task<SshClient> ConnectAsync(this SSHSettings sshSettings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sshSettings);
            TaskCompletionSource<SshClient> tcs = new TaskCompletionSource<SshClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            new Thread(() =>
            {
                SshClient sshClient = null;
                try
                {
                    sshClient = new SshClient(sshSettings.CreateConnectionInfo());
                    sshClient.HostKeyReceived += (object sender, Renci.SshNet.Common.HostKeyEventArgs e) =>
                    {
                        if (sshSettings.TrustedFingerprints.Count == 0)
                        {
                            e.CanTrust = true;
                        }
                        else
                        {
                            e.CanTrust = sshSettings.IsTrustedFingerprint(e.FingerPrint, e.HostKey);
                        }
                    };
                    sshClient.Connect();
                    tcs.TrySetResult(sshClient);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    try
                    {
                        sshClient?.Dispose();
                    }
                    catch { }
                }
            })
            { IsBackground = true }.Start();

            using (cancellationToken.Register(() => { tcs.TrySetCanceled(); }))
            {
                return await tcs.Task;
            }
        }

        public static string EscapeSingleQuotes(this string command)
        {
            return command.Replace("'", "'\"'\"'", StringComparison.OrdinalIgnoreCase);
        }

        public static Task<SSHCommandResult> RunBash(this SshClient sshClient, string command, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(sshClient);
            ArgumentNullException.ThrowIfNull(command);
            command = $"bash -c '{command.EscapeSingleQuotes()}'";
            var sshCommand = sshClient.CreateCommand(command);
            if (timeout is TimeSpan v)
                sshCommand.CommandTimeout = v;
            var tcs = new TaskCompletionSource<SSHCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            new Thread(() =>
            {
                try
                {
                    sshCommand.BeginExecute(ar =>
                    {
                        try
                        {
                            sshCommand.EndExecute(ar);
                            tcs.TrySetResult(CreateSSHCommandResult(sshCommand));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                        finally
                        {
                            sshCommand.Dispose();
                        }
                    });
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            })
            { IsBackground = true }.Start();
            return tcs.Task;
        }

        private static SSHCommandResult CreateSSHCommandResult(SshCommand sshCommand)
        {
            return new SSHCommandResult()
            {
                Output = sshCommand.Result,
                Error = sshCommand.Error,
                ExitStatus = sshCommand.ExitStatus
            };
        }

        public static async Task DisconnectAsync(this SshClient sshClient, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sshClient);

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            new Thread(() =>
            {
                try
                {
                    sshClient.Disconnect();
                    tcs.TrySetResult(true);
                }
                catch
                {
                    tcs.TrySetResult(true); // We don't care about exception
                }
            })
            { IsBackground = true }.Start();
            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                await tcs.Task;
            }
        }
    }
}
