using System.ComponentModel;
using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrackSwap
{
    public partial class AppDialog : Window
    {
        private readonly MessageBoxButton _buttons;
        private readonly MessageBoxImage _image;
        private MessageBoxResult _result = MessageBoxResult.None;
        private bool _resultSet;
        private bool _soundPlayed;

        private AppDialog(
            Window owner,
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            InitializeComponent();
            _buttons = buttons;
            _image = image;
            Owner = owner;
            Title = string.IsNullOrWhiteSpace(title) ? "TrackSwap" : title;
            MessageText.Text = message ?? string.Empty;
            ConfigureIcon(image);
            ConfigureButtons(buttons);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_soundPlayed)
            {
                return;
            }

            _soundPlayed = true;
            switch (_image)
            {
                case MessageBoxImage.Error:
                    SystemSounds.Hand.Play();
                    break;
                case MessageBoxImage.Warning:
                    SystemSounds.Exclamation.Play();
                    break;
                case MessageBoxImage.Information:
                    SystemSounds.Asterisk.Play();
                    break;
                case MessageBoxImage.Question:
                    SystemSounds.Question.Play();
                    break;
            }
        }

        internal static MessageBoxResult Show(
            Window owner,
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            var dialog = new AppDialog(owner, message, title, buttons, image);
            dialog.ShowDialog();
            return dialog._result;
        }

        private void ConfigureIcon(MessageBoxImage image)
        {
            string brushKey;
            switch (image)
            {
                case MessageBoxImage.Error:
                    IconGlyph.Text = "×";
                    brushKey = "DestructiveBrush";
                    break;
                case MessageBoxImage.Warning:
                    IconGlyph.Text = "!";
                    brushKey = "WarningBrush";
                    break;
                case MessageBoxImage.Question:
                    IconGlyph.Text = "?";
                    brushKey = "AccentBrush";
                    break;
                case MessageBoxImage.Information:
                    IconGlyph.Text = "i";
                    brushKey = "AccentBrush";
                    break;
                default:
                    IconColumn.Width = new GridLength(0);
                    IconBorder.Visibility = Visibility.Collapsed;
                    return;
            }

            var brush = Application.Current.TryFindResource(brushKey) as Brush;
            IconBorder.BorderBrush = brush;
            IconGlyph.Foreground = brush;
        }

        private void ConfigureButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    AddButton("确定", MessageBoxResult.OK, isDefault: true, isCancel: true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("确定", MessageBoxResult.OK, isDefault: true);
                    AddButton("取消", MessageBoxResult.Cancel, isCancel: true);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("是", MessageBoxResult.Yes, isDefault: true);
                    AddButton("否", MessageBoxResult.No, isCancel: true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton("是", MessageBoxResult.Yes, isDefault: true);
                    AddButton("否", MessageBoxResult.No);
                    AddButton("取消", MessageBoxResult.Cancel, isCancel: true);
                    break;
            }
        }

        private void AddButton(
            string label,
            MessageBoxResult result,
            bool isDefault = false,
            bool isCancel = false)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 78,
                Margin = ButtonPanel.Children.Count == 0 ? new Thickness(0) : new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel,
                Tag = result
            };
            if (isDefault)
            {
                button.Style = Application.Current.TryFindResource("PrimaryButton") as Style;
            }
            button.Click += DialogButton_Click;
            ButtonPanel.Children.Add(button);
        }

        private void DialogButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MessageBoxResult result)
            {
                _result = result;
                _resultSet = true;
                Close();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_resultSet)
            {
                _result = _buttons == MessageBoxButton.OK
                    ? MessageBoxResult.OK
                    : _buttons == MessageBoxButton.YesNo
                        ? MessageBoxResult.No
                        : MessageBoxResult.Cancel;
                _resultSet = true;
            }
            base.OnClosing(e);
        }
    }
}
