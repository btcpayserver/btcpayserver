using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Storage.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Storage.Services
{
    public class DropFolderHostedService : IHostedService
    {
        private readonly BTCPayServerOptions _BTCPayServerOptions;
        private readonly SettingsRepository _SettingsRepository;
        private readonly FileService _FileService;
        private readonly UserManager<ApplicationUser> _UserManager;
        private ConcurrentQueue<string> uploadQueue = new ConcurrentQueue<string>();
        private StorageSettings _StorageSettings;
        private string adminId;
        private readonly ILogger<DropFolderHostedService> _Logger;

        public DropFolderHostedService(BTCPayServerOptions btcPayServerOptions, SettingsRepository settingsRepository,
            FileService fileService, UserManager<ApplicationUser> userManager, ILogger<DropFolderHostedService> logger)
        {
            _BTCPayServerOptions = btcPayServerOptions;
            _SettingsRepository = settingsRepository;
            _FileService = fileService;
            _UserManager = userManager;
            _Logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var folder = _BTCPayServerOptions.StorageDropFolder;
            if (string.IsNullOrEmpty(folder))
            {
                return Task.CompletedTask;
            }

            var dir = !Directory.Exists(folder) ? Directory.CreateDirectory(folder) : new DirectoryInfo(folder);

            _Logger.LogInformation(
                $"Drop folder configured at {dir.FullName}. Anything moved within it will be uploaded to the configured storage provider.");
            _ = ListenInOnStorageSettings(cancellationToken);
            var watcher = new FileSystemWatcher(folder) {IncludeSubdirectories = true, EnableRaisingEvents = true};
            watcher.Created += WatcherOnCreated;
            UploadAll();
            _ = ProcessQueue(cancellationToken);
            return Task.CompletedTask;
        }

        private async Task ProcessQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (adminId == null)
                {
                    adminId = (await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin)).FirstOrDefault()?.Id;
                    continue;
                }

                if (!uploadQueue.IsEmpty && (_StorageSettings == null || adminId == null))
                {
                    _Logger.LogInformation(
                        "There are queued files in the drop folder but a storage provider is not configured or else there is no server admin registered yet.");
                    await Task.Delay(TimeSpan.FromMinutes(1), token);
                    continue;
                }

                if (!uploadQueue.TryDequeue(out var file)) continue;
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    var storedFile = await _FileService.AddFile(new FileInfo(file), adminId);
                    if (storedFile != null)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception e)
                {
                    _Logger.LogWarning($"Could not upload {file} in drop folder because {e.Message}. Queuing again for later.");
                    uploadQueue.Enqueue(file);
                }
            }
        }


        private async Task ListenInOnStorageSettings(CancellationToken cancellationToken)
        {
            _StorageSettings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            while (!cancellationToken.IsCancellationRequested)
            {
                _StorageSettings = await _SettingsRepository.WaitSettingsChanged<StorageSettings>(cancellationToken);
            }
        }

        private void UploadAll()
        {
            foreach (var file in new DirectoryInfo(_BTCPayServerOptions.StorageDropFolder).GetFiles())
            {
                if (uploadQueue.Contains(file.FullName))
                {
                    continue;
                }

                uploadQueue.Enqueue(file.FullName);
            }
        }

        private void WatcherOnCreated(object sender, FileSystemEventArgs e)
        {
            if (uploadQueue.Contains(e.FullPath))
            {
                return;
            }

            uploadQueue.Enqueue(e.FullPath);
            _Logger.LogInformation($"new file({e.Name}) in drop folder. Queued for upload.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
