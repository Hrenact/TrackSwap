namespace TrackSwap.Models
{
    public sealed class TargetOption
    {
        public TargetOption(string displayName, string targetPath)
        {
            DisplayName = displayName;
            TargetPath = targetPath;
        }

        public string DisplayName { get; }

        public string TargetPath { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
