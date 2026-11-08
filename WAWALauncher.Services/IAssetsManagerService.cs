using System;
using System.Collections.Generic;
using System.Text;

namespace WAWALauncher.Services
{
    public interface IAssetsManagerService
    {
        public Task DownloadAssets();
        public Task UnpackAssets();
    }
}
