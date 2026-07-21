using System.Windows;
using ThermoVision.Camera.Services;
using ThermoVision.Camera.ViewModels;

namespace ThermoVision.Camera
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var cameraService = new DemoCameraService();
            var frameStorage = new JsonFrameStorage();
            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(cameraService, frameStorage)
            };

            mainWindow.Show();
        }
    }
}
