using Downloader;
using System;
using System.Collections.Generic;
using System.Text;
using WAWALauncher.Infrastructure;

namespace WAWALauncher.Services
{
    public class AssetsManagerService : IAssetsManagerService
    {
        private DownloadConfiguration CreateConfiguration(int chunkCount = 8, int maxTryAgainOnFailure = 3)
        {
            var downloadConfig = new DownloadConfiguration
            {
                ChunkCount = 8,
                MaxTryAgainOnFailure = 3,
                RequestConfiguration =
                {
                    UserAgent = $"WAWALauncher/1.0"
                }
            };

            return downloadConfig;
        }
        private async Task Download(string url, string directory, string filename, DownloadConfiguration downloadConfig, IProgress<double> progress, CancellationToken ct)
        {
            using var downloader = DownloadBuilder.New()
                .WithUrl(url)
                .WithDirectory(directory)
                .WithFileName(filename)
                .WithConfiguration(downloadConfig)
                .Build();

            downloader.DownloadProgressChanged += (sender, e) =>
            {
                progress?.Report(e.ProgressPercentage);
            };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await downloader.StartAsync(cts.Token);

                if (downloader.Package.Status == DownloadStatus.Failed)
                {
                    throw new InvalidOperationException("Download failed");
                }

                if (downloader.Package.Status == DownloadStatus.Stopped)
                {
                    throw new OperationCanceledException("Download was canceled by user");
                }
            }

            finally
            {
                downloader.DownloadProgressChanged -= (sender, e) => 
                { 
                    progress?.Report(0); };
            }
        }
        public async Task GetVersionsInformation()
        {
            //await Download(CommonConstants.AssemblyVersionsUrl)
        }
        public Task DownloadAssets()
        {
            throw new NotImplementedException();
        }

        public Task UnpackAssets()
        {
            throw new NotImplementedException();
        }
    }
}
