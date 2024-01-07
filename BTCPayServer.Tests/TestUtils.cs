using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using OpenQA.Selenium;
using Xunit;
using Xunit.Sdk;

namespace BTCPayServer.Tests
{
    public static class TestUtils
    {
#if DEBUG && !SHORT_TIMEOUT
        public const int TestTimeout = 600_000;
#else
        public const int TestTimeout = 90_000;
#endif
        public static DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
        {
            var directory = new DirectoryInfo(
                currentPath ?? Directory.GetCurrentDirectory());
            while (directory != null && !directory.GetFiles("*.sln").Any())
            {
                directory = directory.Parent;
            }
            return directory;
        }


        public static string GetTestDataFullPath(string relativeFilePath)
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !directory.GetFiles("*.csproj").Any())
            {
                directory = directory.Parent;
            }
            return Path.Combine(directory.FullName, "TestData", relativeFilePath);
        }

        public static T AssertType<T>(this object obj)
        {
            Assert.IsType<T>(obj);
            return (T)obj;
        }

        public static FormFile GetFormFile(string filename, string content)
        {
            File.WriteAllText(filename, content);

            var fileInfo = new FileInfo(filename);
            FormFile formFile = new FormFile(
                new FileStream(filename, FileMode.OpenOrCreate),
                0,
                fileInfo.Length, fileInfo.Name, fileInfo.Name)
            {
                Headers = new HeaderDictionary()
            };
            formFile.ContentType = "text/plain";
            formFile.ContentDisposition = $"form-data; name=\"file\"; filename=\"{fileInfo.Name}\"";
            return formFile;
        }
        public static FormFile GetFormFile(string filename, byte[] content)
        {
            File.WriteAllBytes(filename, content);

            var fileInfo = new FileInfo(filename);
            FormFile formFile = new FormFile(
                new FileStream(filename, FileMode.OpenOrCreate),
                0,
                fileInfo.Length, fileInfo.Name, fileInfo.Name)
            {
                Headers = new HeaderDictionary()
            };
            formFile.ContentType = "application/octet-stream";
            formFile.ContentDisposition = $"form-data; name=\"file\"; filename=\"{fileInfo.Name}\"";
            return formFile;
        }
        public static void Eventually(Action act, int ms = 20_000)
        {
            CancellationTokenSource cts = new CancellationTokenSource(ms);
            while (true)
            {
                try
                {
                    act();
                    break;
                }
                catch (WebDriverException) when (!cts.Token.IsCancellationRequested)
                {
                    cts.Token.WaitHandle.WaitOne(500);
                }
                catch (XunitException) when (!cts.Token.IsCancellationRequested)
                {
                    cts.Token.WaitHandle.WaitOne(500);
                }
            }
        }

        public static async Task EventuallyAsync(Func<Task> act, int delay = 20000)
        {
            CancellationTokenSource cts = new CancellationTokenSource(delay);
            while (true)
            {
                try
                {
                    await act();
                    break;
                }
                catch (XunitException) when (!cts.Token.IsCancellationRequested)
                {
                    bool timeout =false;
                    try
                    {
                        await Task.Delay(500, cts.Token);
                    }
                    catch { timeout = true; }
                    if (timeout)
                        throw;
                }
            }
        }

        internal static IHttpClientFactory CreateHttpFactory()
        {
            var services = new ServiceCollection();
            services.AddHttpClient();
            return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        }
    }
}
