using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Windows;

using ScopeDesk.Logging;
using ScopeDesk.Services;
using ScopeDesk.ViewModels;

namespace ScopeDesk
{
    public partial class App : Application
    {
        public IHost? Host { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ScopeConnectionService>();
                    services.AddSingleton<MeasurementService>();

                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .UseSerilog((context, services, configuration) =>
                {
                    SerilogConfig.Configure(configuration, context.Configuration);
                })
                .Build();

            Host.Start();

            var mainWindow = Host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Host.Services.GetRequiredService<MainViewModel>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Host?.Dispose();
            base.OnExit(e);
        }
    }
}
