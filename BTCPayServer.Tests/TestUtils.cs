using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Xunit.Sdk;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Tests
{
    public static class TestUtils
    {
#if DEBUG && !SHORT_TIMEOUT
        public const int TestTimeout = 600_000;
#else
        public const int TestTimeout = 60_000;
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
                catch (XunitException) when (!cts.Token.IsCancellationRequested)
                {
                    cts.Token.WaitHandle.WaitOne(500);
                }
            }
        }

        public static async Task EventuallyAsync(Func<Task> act)
        {
            CancellationTokenSource cts = new CancellationTokenSource(20000);
            while (true)
            {
                try
                {
                    await act();
                    break;
                }
                catch (XunitException) when (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(500);
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
