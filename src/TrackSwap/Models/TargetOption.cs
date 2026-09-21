namespace TrackSwap.Models
{
    public sealed class TargetOption
    {
        public TargetOption(string displayName, string targetPath, bool? isOnline = null)
        {
            DisplayName = displayName;
            TargetPath = targetPath;
            IsOnline = isOnline;
        }

        public string DisplayName { get; }

        public string StatusDisplayName => DeviceOption.BaseDisplayName(DisplayName);

        public string TargetPath { get; }

        public bool? IsOnline { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
