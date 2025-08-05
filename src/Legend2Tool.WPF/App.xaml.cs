using Legend2Tool.WPF.Services;
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
                        // Register your services here
                        services.AddSingleton<IFileService, FileService>();
                        services.AddSingleton<IDialogService, DialogService>();
                        services.AddSingleton<IConfigService, ConfigService>();
                        services.AddSingleton<IEncodingService, EncodingService>();
                        services.AddSingleton<IScriptOptimizationService, ScriptOptimizationService>();

                        services.AddSingleton<ConfigStore>();
                        services.AddSingleton<ProgressStore>();

                        services.AddSingleton<MainViewModel>();
                        services.AddSingleton<MenuViewModel>();
                        services.AddSingleton<PortConfViewModel>();
                        services.AddSingleton<ScriptOptimizationViewModel>();

                        services.AddSingleton<MainWindow>(sp => new MainWindow
                        {
                            DataContext = sp.GetRequiredService<MainViewModel>(),
                        });
                    }
                ).ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    Log.Logger = new LoggerConfiguration()
                                        .WriteTo.File("log.txt", rollingInterval: RollingInterval.Day)
                                        .MinimumLevel.Warning()
                                        .CreateLogger();
                    //logging.AddSerilog(Log.Logger, dispose: true);
                    logging.Services.AddSingleton(Log.Logger);
                });
        }
    }
}
