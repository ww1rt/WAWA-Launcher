using System;
using System.Collections.Generic;
using System.Text;

namespace WAWALauncher.Services
{
    public interface ILauncherService
    {
        Task LaunchGame(string gameParams);
    }
}
