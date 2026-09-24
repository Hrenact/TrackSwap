using System;
using System.Collections.Generic;

namespace TrackSwap.Protocol
{
    public sealed class OscRuntimeStatus
    {
        public bool Enabled { get; set; }
        public bool Listening { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public string SendEndpoint { get; set; } = string.Empty;
        public DateTimeOffset? LastMessageAtUtc { get; set; }
        public string LastError { get; set; } = string.Empty;
        public bool ReceivePortInUse { get; set; }
        public ControllerInputState LeftInput { get; set; } = new ControllerInputState();
        public ControllerInputState RightInput { get; set; } = new ControllerInputState();
        public bool LeftThumbTouchAssist { get; set; }
        public bool LeftIndexTouchAssist { get; set; }
        public bool RightThumbTouchAssist { get; set; }
        public bool RightIndexTouchAssist { get; set; }
        public HapticFeedbackEvent LeftHaptic { get; set; } = new HapticFeedbackEvent();
        public HapticFeedbackEvent RightHaptic { get; set; } = new HapticFeedbackEvent();
        public DateTimeOffset? LeftHapticAtUtc { get; set; }
        public DateTimeOffset? RightHapticAtUtc { get; set; }
        public string LastSendError { get; set; } = string.Empty;
        public List<OscHapticPreviewSample> LeftHapticHistory { get; set; } = new List<OscHapticPreviewSample>();
        public List<OscHapticPreviewSample> RightHapticHistory { get; set; } = new List<OscHapticPreviewSample>();
    }

    public sealed class OscHapticPreviewSample
    {
        public DateTimeOffset OccurredAtUtc { get; set; }
        public float DurationSeconds { get; set; }
        public float Frequency { get; set; }
        public float Amplitude { get; set; }
    }

    public sealed class OscHapticTestRequest
    {
        public ControllerHand Hand { get; set; }
    }
}
