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
            // 공유 DB 경로. 비어있으면 Emulator 공유 DB 자동 탐지, 없으면 로컬 파일 사용
            var dbPath = ConfigurationManager.AppSettings["FlowMaster:DbPath"];
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                var candidates = new[]
                {
                    @"C:\Works\DevSuite\Emulator\data\emulator.db",
                    System.IO.Path.GetFullPath(System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        @"..\..\..\..\Emulator\data\emulator.db"))
                };
                dbPath = System.Array.Find(candidates, System.IO.File.Exists) ?? "flowmaster_test.db";
                AppLogger.Info($"[App] FM DB 자동 탐지: {dbPath}");
            }

            // DB 파일의 디렉토리가 없으면 자동 생성 (경로 지정 시 디렉토리 미존재 오류 방지)
            var dbDir = System.IO.Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir) && !System.IO.Directory.Exists(dbDir))
                System.IO.Directory.CreateDirectory(dbDir);

            // Services
            var approvalRepo = new SqliteApprovalRepository(dbPath);
            services.AddSingleton(approvalRepo);                                          // 구체 타입
            services.AddSingleton<IApprovalRepository>(approvalRepo);                    // 인터페이스
            services.AddSingleton<INotificationService, MockNotificationService>();
            services.AddSingleton<IApprovalService, ApprovalService>();
            services.AddSingleton<ExternalDbRepository>(); // 외부 DB 연동

            // AD 역할 그룹 설정 로드 (Emulator 미실행 시 실제 AD 폴백에 사용)
            var adAdminGroups    = ConfigurationManager.AppSettings["AD:AdminGroups"];
            var adApproverGroups = ConfigurationManager.AppSettings["AD:ApproverGroups"];
            var adFallback = new AdAuthService(adAdminGroups, adApproverGroups);

            AppLogger.Info($"[App] Emulator URL   : {emulatorBaseUrl}");
            AppLogger.Info($"[App] ApprovalApi URL: {approvalApiBaseUrl}");
            AppLogger.Info($"[App] DB Path        : {dbPath}");

            // 인증 서비스: Emulator 실행 시 Emulator, 미실행 시 실제 Windows AD 폴백
            // AppLogger.Info를 로그 콜백으로 주입
            var authService = new EmulatorAuthService(emulatorBaseUrl, adFallback, AppLogger.Info);
            services.AddSingleton<IAuthService>(authService);

            // 앱 등록 사용자/그룹 저장소 (SQLite - FM_AppUsers, FM_AppGroups)
            var appUserRepo = new SqliteAppUserRepository(dbPath);
            services.AddSingleton<SqliteAppUserRepository>(appUserRepo);
            services.AddSingleton<IAppGroupRepository>(appUserRepo);

            // 사용자 저장소: Emulator 실행 시 Emulator, 미실행 시 SQLite 앱 사용자
            services.AddSingleton<IUserRepository>(
                new EmulatorUserRepository(authService, appUserRepo));

            // ApprovalSystem API 클라이언트 (5002 포트)
            services.AddSingleton(new ApprovalApiClient(approvalApiBaseUrl));

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<AdminViewModel>(sp => new AdminViewModel(
                sp.GetRequiredService<IUserRepository>(),
                sp.GetRequiredService<IAppGroupRepository>(),
                adFallback,
                sp.GetRequiredService<SqliteApprovalRepository>()));
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
