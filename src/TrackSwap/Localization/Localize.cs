using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TrackSwap.Localization
{
    public static class Localize
    {
        public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(Localize),
            new PropertyMetadata(null, OnKeyChanged));

        private static readonly List<WeakReference> Targets = new List<WeakReference>();
        private static readonly ConditionalWeakTable<FrameworkElement, MissingVisualState> VisualStates =
            new ConditionalWeakTable<FrameworkElement, MissingVisualState>();
        private static LocalizationService _subscribedService;

        public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

        public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

        private static void OnKeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (!(dependencyObject is FrameworkElement element))
            {
                return;
            }
            EnsureSubscription();
            lock (Targets)
            {
                Targets.Add(new WeakReference(element));
            }
            Apply(element);
        }

        private static void EnsureSubscription()
        {
            LocalizationService service = LocalizationManager.Current;
            if (ReferenceEquals(_subscribedService, service))
            {
                return;
            }
            if (_subscribedService != null)
            {
                _subscribedService.LanguageChanged -= OnLanguageChanged;
            }
            _subscribedService = service;
            _subscribedService.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged(object sender, EventArgs e)
        {
            lock (Targets)
            {
                for (int index = Targets.Count - 1; index >= 0; index--)
                {
                    if (!(Targets[index].Target is FrameworkElement target))
                    {
                        Targets.RemoveAt(index);
                        continue;
                    }
                    Apply(target);
                }
            }
        }

        private static void Apply(FrameworkElement element)
        {
            string key = GetKey(element);
            string value = LocalizationManager.Current.Translate(key);
            if (element is Window window)
            {
                window.Title = value;
            }
            else if (element is TextBlock textBlock)
            {
                textBlock.Text = value;
            }
            else if (element is HeaderedContentControl headered)
            {
                headered.Header = value;
            }
            else if (element is ContentControl contentControl)
            {
                contentControl.Content = value;
            }

            if (LocalizationManager.Current.IsMissing(key))
            {
                EnterMissingState(element, key);
            }
            else
            {
                LeaveMissingState(element);
            }
        }

        private static void EnterMissingState(FrameworkElement element, string key)
        {
            if (!VisualStates.TryGetValue(element, out MissingVisualState state))
            {
                state = new MissingVisualState(element);
                VisualStates.Add(element, state);
                element.PreviewMouseLeftButtonDown += MissingElement_PreviewMouseLeftButtonDown;
            }
            Brush warning = Application.Current?.TryFindResource("WarningBrush") as Brush ?? Brushes.Orange;
            if (element is TextBlock textBlock)
            {
                textBlock.Foreground = warning;
                textBlock.FontFamily = new FontFamily("Consolas");
            }
            else if (element is Control control)
            {
                control.Foreground = warning;
                control.FontFamily = new FontFamily("Consolas");
            }
            element.Cursor = Cursors.Hand;
            element.ToolTip = Tr.Get("language.copy_tooltip") + key;
        }

        private static void LeaveMissingState(FrameworkElement element)
        {
            if (!VisualStates.TryGetValue(element, out MissingVisualState state))
            {
                return;
            }
            state.Restore(element);
            element.PreviewMouseLeftButtonDown -= MissingElement_PreviewMouseLeftButtonDown;
            VisualStates.Remove(element);
        }

        private static void MissingElement_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement element))
            {
                return;
            }
            string key = GetKey(element);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }
            Clipboard.SetText(key);
            e.Handled = true;
        }

        private sealed class MissingVisualState
        {
            private readonly object _foreground;
            private readonly object _fontFamily;
            private readonly object _cursor;
            private readonly object _toolTip;

            public MissingVisualState(FrameworkElement element)
            {
                if (element is TextBlock textBlock)
                {
                    _foreground = textBlock.ReadLocalValue(TextBlock.ForegroundProperty);
                    _fontFamily = textBlock.ReadLocalValue(TextBlock.FontFamilyProperty);
                }
                else if (element is Control control)
                {
                    _foreground = control.ReadLocalValue(Control.ForegroundProperty);
                    _fontFamily = control.ReadLocalValue(Control.FontFamilyProperty);
                }
                _cursor = element.ReadLocalValue(FrameworkElement.CursorProperty);
                _toolTip = element.ReadLocalValue(FrameworkElement.ToolTipProperty);
            }

            public void Restore(FrameworkElement element)
            {
                if (element is TextBlock textBlock)
                {
                    RestoreValue(textBlock, TextBlock.ForegroundProperty, _foreground);
                    RestoreValue(textBlock, TextBlock.FontFamilyProperty, _fontFamily);
                }
                else if (element is Control control)
                {
                    RestoreValue(control, Control.ForegroundProperty, _foreground);
                    RestoreValue(control, Control.FontFamilyProperty, _fontFamily);
                }
                RestoreValue(element, FrameworkElement.CursorProperty, _cursor);
                RestoreValue(element, FrameworkElement.ToolTipProperty, _toolTip);
            }

            private static void RestoreValue(DependencyObject element, DependencyProperty property, object value)
            {
                if (value == null || ReferenceEquals(value, DependencyProperty.UnsetValue))
                {
                    element.ClearValue(property);
                }
                else
                {
                    element.SetValue(property, value);
                }
            }
        }
    }
}
