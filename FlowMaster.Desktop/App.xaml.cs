using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FlowMaster.Core.Interfaces;
using FlowMaster.Core.Services;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Infrastructure.Repositories;
using FlowMaster.Infrastructure.Services;
using FlowMaster.Desktop.ViewModels;

namespace FlowMaster.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider Services { get; }

        public new static App Current => (App)Application.Current;

        public App()
        {
            SQLitePCL.Batteries.Init();
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<IUserRepository, MockUserRepository>();
            services.AddSingleton<IApprovalRepository, SqliteApprovalRepository>();
            services.AddSingleton<INotificationService, MockNotificationService>(); 
            services.AddSingleton<IApprovalService, ApprovalService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<WriteViewModel>();
            services.AddTransient<DetailViewModel>();

            // View
            services.AddTransient<MainWindow>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}
