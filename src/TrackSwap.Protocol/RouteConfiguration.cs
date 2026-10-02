namespace TrackSwap.Protocol
{
    public sealed class RouteConfiguration
    {
        public string RouteId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool PendingDeletion { get; set; }
        public bool HidePhysicalSource { get; set; }
        public bool SplitPoseSource { get; set; }
        public int VirtualDeviceSlot { get; set; }

        /// <summary>
        /// Missing values in configurations created before v006 deserialize as ReplaceTarget,
        /// preserving the behavior of existing routes.
        /// </summary>
        public RouteMode Mode { get; set; } = RouteMode.ReplaceTarget;

        /// <summary>Required only for VirtualController routes.</summary>
        public ControllerHand ControllerHand { get; set; }

        /// <summary>Required only for VirtualController routes.</summary>
        public ControlInputSource ControlInputSource { get; set; }

        /// <summary>
        /// Selects whether the route reads an OpenVR device or publishes a persisted fixed pose.
        /// Manual pose is currently supported only by virtual-HMD routes.
        /// </summary>
        public PoseSourceKind PoseSourceKind { get; set; } = PoseSourceKind.Device;

        /// <summary>Exact registered device path returned by OpenVR.</summary>
        public string SourceDevicePath { get; set; } = string.Empty;

        /// <summary>
        /// Exact registered device path used for orientation when SplitPoseSource is enabled.
        /// Empty means the position source also provides orientation.
        /// </summary>
        public string RotationSourceDevicePath { get; set; } = string.Empty;

        /// <summary>Target used by ReplaceTarget routes; empty for direct-output routes.</summary>
        public string TargetDevicePath { get; set; } = string.Empty;

        public PoseOffset Offset { get; set; } = PoseOffset.Identity();

        public MotionSmoothingConfiguration MotionSmoothing { get; set; } =
            new MotionSmoothingConfiguration();

        /// <summary>
        /// Absolute standing-space pose used when PoseSourceKind is Manual.
        /// Translation is expressed in centimetres.
        /// </summary>
        public PoseOffset ManualPose { get; set; } = PoseOffset.DefaultManualPose();
    }
}
