using BTCPayServer.Tests.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace BTCPayServer.Tests
{
    public class ProcessLauncher
    {
        string _CurrentDirectory;
        public string CurrentDirectory
        {
            get
            {
                return _CurrentDirectory;
            }
        }
        public ProcessLauncher()
        {
            _CurrentDirectory = Directory.GetCurrentDirectory();
        }
        public bool GoTo(string[] directories, bool createIfNotExists = false)
        {
            var original = _CurrentDirectory;
            foreach (var dir in directories)
            {
                var newDirectory = Path.Combine(_CurrentDirectory, dir);
                if (!Directory.Exists(newDirectory))
                {
                    if (createIfNotExists)
                        Directory.CreateDirectory(newDirectory);
                    else
                    {
                        _CurrentDirectory = original;
                        return false;
                    }
                }
                _CurrentDirectory = newDirectory;
            }
            return true;
        }

        Stack<string> _Directories = new Stack<string>();
        public void PushDirectory()
        {
            _Directories.Push(_CurrentDirectory);
        }

        public void PopDirectory()
        {
            _CurrentDirectory = _Directories.Pop();
        }

        public bool GoTo(string directory, bool createIfNotExists = false)
        {
            return GoTo(new[] { directory }, createIfNotExists);
        }

        public void Run(string processName, string args)
        {
            Start(processName, args).WaitForExit();
        }

        public Process Start(string processName, string args)
        {
            Logs.Tester.LogInformation($"Running [{processName} {args}] from {_CurrentDirectory}");
            StringBuilder builder = new StringBuilder();
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    WorkingDirectory = _CurrentDirectory,
                    FileName = processName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            try
            {
                process.OutputDataReceived += (s, e) =>
                {
                    Logs.Tester.LogInformation(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    Logs.Tester.LogInformation(e.Data);
                };
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception ex) { throw new Exception($"You need to install {processName} for this test (info : {ex.Message})"); }
            return process;
        }

        public void AssertExists(string file)
        {
            var path = Path.Combine(_CurrentDirectory, file);
            Assert.True(File.Exists(path), $"The file {path} should exist");
        }

        public bool Exists(string file)
        {
            var path = Path.Combine(_CurrentDirectory, file);
            return File.Exists(path);
        }
    }
}
