using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TrackSwap.Localization;
using TrackSwap.Services;

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
            EventManager.RegisterClassHandler(
                typeof(ComboBoxItem),
                FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler(OnComboBoxItemRequestBringIntoView));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            string waitValue = e.Args
                .SkipWhile(argument => !string.Equals(argument, "--wait-for-pid", System.StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .FirstOrDefault();
            if (int.TryParse(waitValue, out int waitProcessId))
            {
                try
                {
                    Process.GetProcessById(waitProcessId).WaitForExit(15000);
                }
                catch (System.ArgumentException)
                {
                }
                catch (System.InvalidOperationException)
                {
                }
            }

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

            var preferencesService = new UiPreferencesService();
            string locale = preferencesService.Load().LanguageLocale;
            var localizationService = new LocalizationService();
            localizationService.SetLanguage(locale);
            LocalizationManager.Initialize(localizationService);

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

        private static void OnComboBoxItemRequestBringIntoView(
            object sender,
            RequestBringIntoViewEventArgs e)
        {
            // WPF focuses a ComboBoxItem whenever the pointer enters it. A partially
            // visible first or last item then requests scrolling even though the user
            // only hovered it. Keep explicit wheel/scrollbar and keyboard navigation,
            // but suppress this hover-only adjustment.
            if (sender is ComboBoxItem item &&
                item.IsMouseOver &&
                ItemsControl.ItemsControlFromItemContainer(item) is ComboBox comboBox &&
                comboBox.IsDropDownOpen)
            {
                e.Handled = true;
            }
        }

    }
}
