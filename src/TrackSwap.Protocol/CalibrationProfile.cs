using System;
using System.Collections.Generic;

namespace TrackSwap.Protocol
{
    public sealed class CalibrationProfile
    {
        public string ProfileId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SourceDevicePath { get; set; } = string.Empty;
        public string TargetDevicePath { get; set; } = string.Empty;
        public DateTimeOffset CapturedAtUtc { get; set; }
        public int SampleCount { get; set; }
        public double TranslationRmsMetres { get; set; }
        public double RotationRmsDegrees { get; set; }
        public PoseOffset Offset { get; set; } = PoseOffset.Identity();

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class CalibrationCaptureRequest
    {
        public string ProfileName { get; set; } = string.Empty;
        public string SourceDevicePath { get; set; } = string.Empty;
        public string TargetDevicePath { get; set; } = string.Empty;
        public bool OriginalTargetPoseVisible { get; set; }
        public int RequestedSampleCount { get; set; } = 90;
    }

    public sealed class CalibrationCaptureResponse
    {
        public CalibrationProfile Profile { get; set; } = new CalibrationProfile();
    }

    public sealed class CalibrationProfilesSnapshot
    {
        public List<CalibrationProfile> Profiles { get; set; } = new List<CalibrationProfile>();
    }

    public sealed class CalibrationProfileRequest
    {
        public string ProfileId { get; set; } = string.Empty;
    }
}
