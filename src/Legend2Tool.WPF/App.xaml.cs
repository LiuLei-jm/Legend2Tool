using Legend2Tool.WPF.Services.ApplicationSettings;
using Legend2Tool.WPF.Services.Authentication;
using Legend2Tool.WPF.Services.DynamicMonsterSpawning;
using Legend2Tool.WPF.Services.Infrastructure.Files;
using Legend2Tool.WPF.Services.Infrastructure.Logging;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.Presentation;
using Legend2Tool.WPF.Services.ScriptOptimization;
using Legend2Tool.WPF.Services.ScriptOptimization.Modular;
using Legend2Tool.WPF.Services.ScriptSets;
using Legend2Tool.WPF.Services.ServerConfiguration;
using Legend2Tool.WPF.State;
using Legend2Tool.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Windows;

namespace Legend2Tool.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [STAThread]
        static void Main(string[] args)
        {
            using var host = CreateHostBuilder(args).Build();
            host.Start();
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var app = new App();
            app.InitializeComponent();
            app.MainWindow = host.Services.GetRequiredService<MainWindow>();
            app.MainWindow.Visibility = Visibility.Visible;
            app.Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(
                    (context, services) =>
                    {
                        services.AddSingleton<ConfigStore>();
                        services.AddSingleton<ProgressStore>();
                        services.AddSingleton<AppConfigService>();
                        services.AddSingleton(sp =>
                            sp.GetRequiredService<AppConfigService>().LoadOrCreate()
                        );
                        // Register your services here
                        services.AddSingleton<IFileService, FileService>();
                        services.AddSingleton<IDialogService, DialogService>();
                        services.AddSingleton<IConfigService, ConfigService>();
                        services.AddSingleton<IEncodingService, EncodingService>();
                        services.AddSingleton<ICredentialStore, CredentialStore>();
                        services.AddSingleton<IAuthenticationService, AuthenticationService>();
                        services.AddSingleton<IScriptSetService, ScriptSetService>();
                        services.AddSingleton<
                            IScriptSetInstallationService,
                            ScriptSetInstallationService
                        >();
                        services.AddSingleton<
                            IDynamicMonsterSpawningService,
                            DynamicMonsterSpawningService
                        >();
                        services.AddSingleton<
                            IScriptOptimizationService,
                            ModularScriptOptimizationService
                        >();

                        services.AddSingleton<MainViewModel>();
                        services.AddSingleton<MenuViewModel>();
                        services.AddSingleton<PortConfViewModel>();
                        services.AddSingleton<DynamicMonsterSpawningViewModel>();
                        services.AddSingleton<InsertScriptViewModel>();
                        services.AddSingleton<ScriptOptimizationViewModel>();
                        services.AddSingleton<LogViewModel>();

                        services.AddSingleton<MainWindow>(sp => new MainWindow
                        {
                            DataContext = sp.GetRequiredService<MainViewModel>(),
                        });
                    }
                )
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    Log.Logger = new LoggerConfiguration()
                        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                        .WriteTo.Sink(LogSink.Create())
                        .MinimumLevel.Warning()
                        .CreateLogger();
                    logging.Services.AddSingleton(Log.Logger);
                });
        }
    }
}
