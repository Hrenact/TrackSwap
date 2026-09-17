namespace TrackSwap.Models
{
    public sealed class TrackingOverrideOption
    {
        public TrackingOverrideOption(
            string sourceName,
            string sourcePath,
            string targetName,
            string targetPath)
        {
            SourceName = sourceName;
            SourcePath = sourcePath;
            TargetName = targetName;
            TargetPath = targetPath;
        }

        public string SourceName { get; }

        public string SourcePath { get; }

        public string TargetName { get; }

        public string TargetPath { get; }
    }
}
