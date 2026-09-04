// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0.
// COPIED FROM https://github.com/dotnet/sdk/blob/main/src/BuiltInTools/dotnet-watch/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services;

public sealed record HostCommandResult(int ExitCode, string Output, string Error);
public class ProcessRunner(ILoggerFactory loggerFactory, IConfiguration conf)
{
    public bool BTCPayHostEnabled { get; set; } = conf.GetOrDefault("btcpayhostenabled", false);
    public string BTCPayHostExecutable { get; set; } = conf["btcpayhostexecutable"] ?? "btcpay-host";
    private readonly ILogger _logger = loggerFactory.CreateLogger("BTCPayServer.ProcessRunner");

    #nullable enable
    public async Task<HostCommandResult> RunHostCommand(string hostCommand, IReadOnlyList<string>? arguments, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        return await RunHostCommand(hostCommand, arguments, timeoutCts.Token);
    }

    public async Task<HostCommandResult> RunHostCommand(string hostCommand, IReadOnlyList<string>? arguments, CancellationToken cancellationToken)
    {
        var output = new OutputCapture();
        var error = new OutputCapture();
        var args = arguments?.ToList() ?? new();
        args.Insert(0, hostCommand);
        var exitCode = await RunAsync(new ProcessSpec
        {
            Executable = BTCPayHostExecutable,
            Arguments = args,
            OutputCapture = output,
            ErrorCapture = error
        }, cancellationToken);
        return new HostCommandResult(exitCode, output.ToString(), error.ToString());
    }
#nullable restore
    public async Task<int> RunAsync(ProcessSpec processSpec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processSpec);

        int exitCode;
        var stopwatch = new Stopwatch();

        using (var process = CreateProcess(processSpec))
        using (var processState = new ProcessState(process, _logger))
        using (cancellationToken.Register(() => processState.TryKill()))
        {
            var readOutput = false;
            var readError = false;
            if (processSpec.IsErrorCaptured)
            {
                readError = true;
                process.ErrorDataReceived += (_, a) =>
                {
                    if (a.Data is not null)
                        processSpec.ErrorCapture.AddLine(a.Data);
                };
            }
            if (processSpec.IsOutputCaptured)
            {
                readOutput = true;
                process.OutputDataReceived += (_, a) =>
                {
                    if (a.Data is not null)
                        processSpec.OutputCapture.AddLine(a.Data);
                };
            }

            stopwatch.Start();
            var arguments = process.StartInfo.Arguments != "" ? process.StartInfo.Arguments : string.Join(" ", process.StartInfo.ArgumentList);
            _logger.LogInformation($"Running '{processSpec.Executable} {arguments}'");

            process.Start();

            if (readOutput)
                process.BeginOutputReadLine();
            if (readError)
                process.BeginErrorReadLine();

            if (processSpec.Stdin is not null)
            {
                foreach (var line in processSpec.Stdin)
                    await process.StandardInput.WriteLineAsync(line);
                process.StandardInput.Close();
            }
            else if (processSpec.StdinContent is not null)
            {
                await process.StandardInput.WriteAsync(processSpec.StdinContent);
                process.StandardInput.Close();
            }

            await processState.Task;

            exitCode = process.ExitCode;
            stopwatch.Stop();
            _logger.LogInformation($"Process return {exitCode} and ran for {stopwatch.ElapsedMilliseconds}ms");
        }

        return exitCode;
    }

    private System.Diagnostics.Process CreateProcess(ProcessSpec processSpec)
    {
        var process = new System.Diagnostics.Process
        {
            EnableRaisingEvents = true,
            StartInfo =
            {
                FileName = processSpec.Executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = processSpec.WorkingDirectory,
                RedirectStandardOutput = processSpec.IsOutputCaptured,
                RedirectStandardError = processSpec.IsErrorCaptured,
                RedirectStandardInput = processSpec.Stdin is not null || processSpec.StdinContent is not null
            }
        };

        if (processSpec.EscapedArguments is not null)
        {
            process.StartInfo.Arguments = processSpec.EscapedArguments;
        }
        else if (processSpec.Arguments is not null)
        {
            for (var i = 0; i < processSpec.Arguments.Count; i++)
                process.StartInfo.ArgumentList.Add(processSpec.Arguments[i]);
        }

        foreach (var env in processSpec.EnvironmentVariables)
            process.StartInfo.Environment[env.Key] = env.Value;

        SetEnvironmentVariable(process.StartInfo, "DOTNET_STARTUP_HOOKS", processSpec.EnvironmentVariables.DotNetStartupHooks, Path.PathSeparator);
        SetEnvironmentVariable(process.StartInfo, "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", processSpec.EnvironmentVariables.AspNetCoreHostingStartupAssemblies, ';');

        return process;
    }

    private static void SetEnvironmentVariable(ProcessStartInfo processStartInfo, string envVarName, List<string> envVarValues, char separator)
    {
        if (envVarValues is { Count: 0 })
            return;

        var existing = Environment.GetEnvironmentVariable(envVarName);
        var result = !string.IsNullOrEmpty(existing) ? existing + separator + string.Join(separator, envVarValues) : string.Join(separator, envVarValues);
        processStartInfo.EnvironmentVariables[envVarName] = result;
    }

    private class ProcessState : IDisposable
    {
        private readonly ILogger _logger;
        private readonly System.Diagnostics.Process _process;
        private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();
        private volatile bool _disposed;

        public ProcessState(System.Diagnostics.Process process, ILogger logger)
        {
            _logger = logger;
            _process = process;
            _process.Exited += OnExited;
            Task = _tcs.Task.ContinueWith(_ =>
            {
                try
                {
                    if (!_process.WaitForExit(int.MaxValue))
                        throw new TimeoutException();
                    _process.WaitForExit();
                }
                catch (InvalidOperationException) { }
            });
        }

        public Task Task { get; }

        public void TryKill()
        {
            if (_disposed)
                return;

            try
            {
                if (!_process.HasExited)
                {
                    _logger.LogInformation($"Killing process {_process.Id}");
                    _process.Kill();
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Error while killing process '{_process.StartInfo.FileName} {_process.StartInfo.Arguments}': {ex.Message}");
            }
        }

        private void OnExited(object sender, EventArgs args) => _tcs.TrySetResult(null);

        public void Dispose()
        {
            if (_disposed)
                return;
            TryKill();
            _disposed = true;
            _process.Exited -= OnExited;
            _process.Dispose();
        }
    }
}

public class ProcessSpec
{
    public string Executable { get; set; }
    public string WorkingDirectory { get; set; }
    public ProcessSpecEnvironmentVariables EnvironmentVariables { get; } = new ProcessSpecEnvironmentVariables();
    public IReadOnlyList<string> Arguments { get; set; }
    public string EscapedArguments { get; set; }
    public OutputCapture OutputCapture { get; set; }
    public OutputCapture ErrorCapture { get; set; }
    public bool IsOutputCaptured => OutputCapture != null;
    public bool IsErrorCaptured => ErrorCapture != null;
    public string[] Stdin { get; set; }
    public string StdinContent { get; set; }

    public string ShortDisplayName() => Path.GetFileNameWithoutExtension(Executable);

    public sealed class ProcessSpecEnvironmentVariables : Dictionary<string, string>
    {
        public List<string> DotNetStartupHooks { get; } = new List<string>();
        public List<string> AspNetCoreHostingStartupAssemblies { get; } = new List<string>();
    }
}

public class OutputCapture
{
    private readonly List<string> _lines = new List<string>();
    public IEnumerable<string> Lines => _lines;
    public void AddLine(string line) => _lines.Add(line);
    public override string ToString() => _lines.Count == 0 ? string.Empty : string.Join('\n', _lines) + '\n';
}
