using System.Windows;
using TrackSwap.Localization;

namespace TrackSwap
{
    public partial class DeviceMetadataWindow : Window
    {
        public DeviceMetadataWindow(
            Window owner,
            string heading,
            string identifier,
            string customDisplayName,
            string note)
        {
            InitializeComponent();
            Owner = owner;
            Title = Tr.Get("settings.devices.metadata.window_title");
            HeadingText.Text = heading ?? string.Empty;
            IdentifierText.Text = identifier ?? string.Empty;
            DisplayNameTextBox.Text = customDisplayName ?? string.Empty;
            NoteTextBox.Text = note ?? string.Empty;
            Loaded += (_, __) =>
            {
                DisplayNameTextBox.SelectAll();
                DisplayNameTextBox.Focus();
            };
        }

        public string CustomDisplayName => DisplayNameTextBox.Text.Trim();

        public string Note => NoteTextBox.Text.Trim();

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
