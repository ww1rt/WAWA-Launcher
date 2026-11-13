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

        [ObservableProperty]
        private bool _isLaunching;

        [ObservableProperty]
        private string _statusText = "Готов к запуску";


        public MainWindowViewModel(ILauncherService launcherService)
        {
            _launcherService = launcherService;
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
    }
}
