using Microsoft.Extensions.DependencyInjection;

namespace app
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogCrash(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
                LogCrash(e.Exception);
        }

        private static void LogCrash(Exception? ex)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "gallery_crash.txt");
                File.WriteAllText(path, ex?.ToString() ?? "Unknown exception");
            }
            catch { }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}