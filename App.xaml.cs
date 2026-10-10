using System.Windows;
using Chassis_Master_Test_Suite.Themes;

namespace Chassis_Master_Test_Suite
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppearanceService.Initialize();
            base.OnStartup(e);
        }
    }
}
