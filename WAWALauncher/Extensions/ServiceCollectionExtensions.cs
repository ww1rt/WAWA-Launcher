using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using WAWALauncher.ViewModels;

namespace WAWALauncher.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddViewModels (this IServiceCollection viewModels)
        {
            viewModels.AddTransient<MainWindowViewModel>();
        }

        public static void AddServices(this IServiceCollection services)
        {

        }
    }
}
