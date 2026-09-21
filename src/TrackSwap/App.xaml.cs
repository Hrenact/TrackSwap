using System.Threading;
using System.Windows;

namespace TrackSwap
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        public App()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\TrackSwap.UI.v1",
                createdNew: out bool createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
            base.OnExit(e);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                WindowThemeService.ApplyDarkTitleBar(window);
            }
        }
    }
}
