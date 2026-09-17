using System;

namespace TrackSwap.Models
{
    public sealed class BackupOption
    {
        public BackupOption(
            string filePath,
            DateTime backupTime,
            long fileSize,
            int ruleCount,
            string summary,
            bool isValid,
            string validationMessage)
        {
            FilePath = filePath;
            BackupTime = backupTime;
            FileSize = fileSize;
            RuleCount = ruleCount;
            Summary = summary;
            IsValid = isValid;
            ValidationMessage = validationMessage;
        }

        public string FilePath { get; }

        public DateTime BackupTime { get; }

        public long FileSize { get; }

        public int RuleCount { get; }

        public string Summary { get; }

        public bool IsValid { get; }

        public string ValidationMessage { get; }

        public string DisplayTime => BackupTime.ToString("yyyy-MM-dd  HH:mm:ss");

        public string DisplaySize => FileSize < 1024
            ? FileSize + " B"
            : (FileSize / 1024.0).ToString("0.0") + " KB";

        public string RuleCountText => IsValid ? RuleCount + " 条规则" : "无法读取";
    }
}
