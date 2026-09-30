using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace TrackSwap.Localization
{
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var value = new LocalizedBindingValue(Key);
            return new Binding(nameof(LocalizedBindingValue.Value))
            {
                Source = value,
                Mode = BindingMode.OneWay
            }.ProvideValue(serviceProvider);
        }
    }

    internal sealed class LocalizedBindingValue : INotifyPropertyChanged
    {
        private readonly string _key;

        public LocalizedBindingValue(string key)
        {
            _key = key;
            LocalizationManager.Current.LanguageChanged += OnLanguageChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Value => LocalizationManager.Current.Translate(_key);

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    internal static class Tr
    {
        public static string Get(string key)
        {
            return LocalizationManager.Current.Translate(key);
        }
    }
}
