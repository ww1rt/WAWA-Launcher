using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using WAWALauncher.Services;

namespace WAWALauncher.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ILauncherService _launcherService;
        private readonly IAssetsManagerService _assetsManagerService;

        [ObservableProperty]
        private bool _isLaunching;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private string _statusText = "Готов к запуску";

        [ObservableProperty]
        private double _downloadProgress = 0;


        public MainWindowViewModel(ILauncherService launcherService, IAssetsManagerService assetsManagerService)
        {
            _launcherService = launcherService;
            _assetsManagerService = assetsManagerService;
        }

        [RelayCommand(CanExecute = nameof(CanLaunch))]
        private async Task PlayAsync(CancellationToken ct)
        {
            try
            {
                IsLaunching = true;
                StatusText = "Запуск игры...";

                // Твой асинхронный код
                await _launcherService.LaunchGame("");

                StatusText = "Игра запущена";
            }

            catch (OperationCanceledException)
            {
                StatusText = "Запуск отменён";
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsLaunching = false;
                PlayCommand.NotifyCanExecuteChanged(); // Обновляем состояние кнопки
            }
        }

        // Метод для CanExecute
        private bool CanLaunch() => !IsLaunching;

        [RelayCommand(CanExecute = nameof(CanDownload))]
        private async Task DownloadAsync(CancellationToken ct) 
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusText = "Проверка обновлений...";
            });

            try
            {
                IsDownloading = true;
                await _assetsManagerService.GetVersionsInformation(ct);
                StatusText = "Версии получены";
                DownloadProgress = 100;
            }
            catch (InvalidOperationException ex) 
            {
                StatusText = ex.Message;
                IsDownloading = false;
                DownloadProgress = 0;
            }
            catch (OperationCanceledException ex)
            {
                StatusText = ex.Message;
                IsDownloading = false;
                DownloadProgress = 0;
            }
            catch (Exception ex) 
            {
                StatusText = $"Произошла неизвестная ошибка. Подробности...\n{ex.Message}";
                IsDownloading = false;
                DownloadProgress = 0;
            }

        }

        private bool CanDownload => !IsDownloading;
    }
}
