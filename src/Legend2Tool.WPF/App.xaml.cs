using System.Windows;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
                        services.AddSingleton<IDialogService, DialogService>();

                        services.AddSingleton<MainViewModel>();
                        services.AddSingleton<MenuViewModel>();
                        services.AddSingleton<PortConfViewModel>();

                        services.AddSingleton<MainWindow>(sp => new MainWindow
                        {
                            DataContext = sp.GetRequiredService<MainViewModel>(),
                        });
                    }
                );
        }
    }
}
