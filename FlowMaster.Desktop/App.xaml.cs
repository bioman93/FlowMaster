using System;
using System.Configuration;
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

            // App.config 설정값 로드
            var approvalApiBaseUrl = ConfigurationManager.AppSettings["ApprovalApi:BaseUrl"]
                ?? "http://localhost:5002/api";
            var emulatorBaseUrl = ConfigurationManager.AppSettings["Emulator:BaseUrl"]
                ?? "http://localhost:3900";
            // 공유 DB 경로. 비어있으면 로컬 파일 사용
            var dbPath = ConfigurationManager.AppSettings["FlowMaster:DbPath"];
            if (string.IsNullOrWhiteSpace(dbPath))
                dbPath = "flowmaster_test.db";

            // Services
            var approvalRepo = new SqliteApprovalRepository(dbPath);
            services.AddSingleton(approvalRepo);                                          // 구체 타입
            services.AddSingleton<IApprovalRepository>(approvalRepo);                    // 인터페이스
            services.AddSingleton<INotificationService, MockNotificationService>();
            services.AddSingleton<IApprovalService, ApprovalService>();
            services.AddSingleton<ExternalDbRepository>(); // 외부 DB 연동

            // 인증 서비스: Emulator 연동, 미실행 시 Mock 폴백
            var authService = new EmulatorAuthService(emulatorBaseUrl);
            services.AddSingleton<IAuthService>(authService);

            // 사용자 저장소: EmulatorAuthService 위임 (GetUsersAsync → Emulator)
            services.AddSingleton<IUserRepository>(
                new EmulatorUserRepository(authService));

            // ApprovalSystem API 클라이언트 (5002 포트)
            services.AddSingleton(new ApprovalApiClient(approvalApiBaseUrl));

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
                sp.GetRequiredService<IApprovalRepository>(),
                sp.GetRequiredService<IUserRepository>(),
                sp.GetRequiredService<ApprovalApiClient>()));
            services.AddTransient<WriteViewModel>(sp => new WriteViewModel(
                sp.GetRequiredService<IApprovalService>(),
                sp.GetRequiredService<IApprovalRepository>(),
                sp.GetRequiredService<IUserRepository>(),
                sp.GetRequiredService<ApprovalApiClient>()));
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
