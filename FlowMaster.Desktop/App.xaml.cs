using System;
using System.Configuration;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FlowMaster.Core.Interfaces;
using FlowMaster.Core.Services;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Infrastructure.Repositories;
using FlowMaster.Infrastructure.Services;
using FlowMaster.Desktop.Services;
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
            AppLogger.Info("========================================");
            AppLogger.Info("[App] FlowMaster 시작");
            AppLogger.Info($"[App] 실행 경로: {AppDomain.CurrentDomain.BaseDirectory}");
            AppLogger.Info($"[App] OS: {Environment.OSVersion}");
            AppLogger.Info($"[App] 사용자: {Environment.UserDomainName}\\{Environment.UserName}");
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

            // ApprovalSystem API 클라이언트 (5002 포트) — FM 데이터 접근 전용
            var apiClient = new ApprovalApiClient(approvalApiBaseUrl);
            services.AddSingleton(apiClient);

            // IApprovalRepository → ApiApprovalRepository (HTTP → ApprovalSystem /api/fm/*)
            var approvalRepo = new ApiApprovalRepository(apiClient);
            services.AddSingleton<IApprovalRepository>(approvalRepo);

            services.AddSingleton<INotificationService, MockNotificationService>();
            services.AddSingleton<IApprovalService, ApprovalService>();
            services.AddSingleton<ExternalDbRepository>(); // 외부 DB 연동

            // AD 역할 그룹 설정 로드 (Emulator 미실행 시 실제 AD 폴백에 사용)
            var adAdminGroups    = ConfigurationManager.AppSettings["AD:AdminGroups"];
            var adApproverGroups = ConfigurationManager.AppSettings["AD:ApproverGroups"];
            var adFallback = new AdAuthService(adAdminGroups, adApproverGroups);

            AppLogger.Info($"[App] Emulator URL   : {emulatorBaseUrl}");
            AppLogger.Info($"[App] ApprovalApi URL: {approvalApiBaseUrl}");

            // 인증 서비스: Emulator 실행 시 Emulator, 미실행 시 실제 Windows AD 폴백
            // AppLogger.Info를 로그 콜백으로 주입
            var authService = new EmulatorAuthService(emulatorBaseUrl, adFallback, AppLogger.Info);
            services.AddSingleton<IAuthService>(authService);

            // 앱 등록 사용자/그룹 저장소 (API - FM_AppUsers, FM_AppGroups via ApprovalSystem)
            var appUserRepo = new ApiAppUserRepository(apiClient);
            services.AddSingleton<IAppGroupRepository>(appUserRepo);

            // 사용자 저장소: Emulator 실행 시 Emulator, 미실행 시 API 앱 사용자 (공유)
            services.AddSingleton<IUserRepository>(
                new EmulatorUserRepository(authService, appUserRepo));

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<AdminViewModel>(sp => new AdminViewModel(
                sp.GetRequiredService<IUserRepository>(),
                sp.GetRequiredService<IAppGroupRepository>(),
                adFallback,
                sp.GetRequiredService<IApprovalRepository>(),
                sp.GetRequiredService<ApprovalApiClient>()));
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
