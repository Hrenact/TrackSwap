using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnLocalizedFallbackClicked));
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

        private static void OnLocalizedFallbackClicked(object sender, MouseButtonEventArgs e)
        {
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null)
            {
                string text = current is TextBlock textBlock
                    ? textBlock.Text
                    : current is ContentControl contentControl && contentControl.Content is string content
                        ? content
                        : null;
                if (TryExtractFallbackKey(text, out string key))
                {
                    Clipboard.SetText(key);
                    e.Handled = true;
                    return;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private static bool TryExtractFallbackKey(string value, out string key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length < 3 ||
                value[0] != '⟦' || value[value.Length - 1] != '⟧')
            {
                return false;
            }
            key = value.Substring(1, value.Length - 2);
            return key.Length != 0;
        }
    }
}
