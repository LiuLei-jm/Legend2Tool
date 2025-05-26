using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace Legend2Tool.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            Services = ConfigureServices();

            this.InitializeComponent();
        }

        public static new App Current => (App)Application.Current;
        public IServiceProvider Services { get; }

        private static IServiceProvider? ConfigureServices()
        {
            var services = new ServiceCollection();
            return services.BuildServiceProvider();
        }
    }
}
