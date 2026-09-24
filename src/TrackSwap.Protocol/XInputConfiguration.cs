using System;

namespace TrackSwap.Protocol
{
    public enum XInputBindingSource
    {
        None = 0,
        LeftStick = 1,
        RightStick = 2,
        LeftStickClick = 3,
        RightStickClick = 4,
        LeftShoulder = 5,
        RightShoulder = 6,
        LeftTrigger = 7,
        RightTrigger = 8,
        View = 9,
        Menu = 10,
        DPadUp = 11,
        DPadDown = 12,
        DPadLeft = 13,
        DPadRight = 14,
        A = 15,
        B = 16,
        X = 17,
        Y = 18
    }

    public enum XInputHapticMode
    {
        PreserveHandedness = 0,
        MirrorHandedness = 1,
        Unified = 2
    }

    public sealed class XInputHandMapping
    {
        public XInputBindingSource PrimaryButton { get; set; }
        public XInputBindingSource SecondaryButton { get; set; }
        public XInputBindingSource Joystick { get; set; }
        public XInputBindingSource JoystickClick { get; set; }
        public XInputBindingSource Trigger { get; set; }
        public XInputBindingSource Grip { get; set; }
        public XInputBindingSource MenuButton { get; set; }
        public XInputTouchAssistMapping ThumbTouch { get; set; } = new XInputTouchAssistMapping();
        public XInputTouchAssistMapping IndexTouch { get; set; } = new XInputTouchAssistMapping();
    }

    public sealed class XInputTouchAssistMapping
    {
        public XInputBindingSource ToggleSource { get; set; }
        public bool DefaultTouched { get; set; } = true;
    }

    public sealed class XInputConfiguration
    {
        public const float DefaultAnalogPressThreshold = 0.5f;
        public const float MinimumAnalogPressThreshold = 0.05f;
        public const float MaximumAnalogPressThreshold = 0.95f;

        public float AnalogPressThreshold { get; set; } = DefaultAnalogPressThreshold;
        public XInputHapticMode HapticMode { get; set; } = XInputHapticMode.PreserveHandedness;
        public XInputHandMapping Left { get; set; } = CreateDefaultLeftMapping();
        public XInputHandMapping Right { get; set; } = CreateDefaultRightMapping();

        public static XInputConfiguration CreateDefault()
        {
            return new XInputConfiguration();
        }

        public static XInputHandMapping CreateDefaultLeftMapping()
        {
            return new XInputHandMapping
            {
                PrimaryButton = XInputBindingSource.X,
                SecondaryButton = XInputBindingSource.Y,
                Joystick = XInputBindingSource.LeftStick,
                JoystickClick = XInputBindingSource.LeftStickClick,
                Trigger = XInputBindingSource.LeftShoulder,
                Grip = XInputBindingSource.LeftTrigger,
                MenuButton = XInputBindingSource.View,
                ThumbTouch = new XInputTouchAssistMapping
                {
                    ToggleSource = XInputBindingSource.DPadUp,
                    DefaultTouched = true
                },
                IndexTouch = new XInputTouchAssistMapping
                {
                    ToggleSource = XInputBindingSource.DPadLeft,
                    DefaultTouched = true
                }
            };
        }

        public static XInputHandMapping CreateDefaultRightMapping()
        {
            return new XInputHandMapping
            {
                PrimaryButton = XInputBindingSource.A,
                SecondaryButton = XInputBindingSource.B,
                Joystick = XInputBindingSource.RightStick,
                JoystickClick = XInputBindingSource.RightStickClick,
                Trigger = XInputBindingSource.RightShoulder,
                Grip = XInputBindingSource.RightTrigger,
                MenuButton = XInputBindingSource.Menu,
                ThumbTouch = new XInputTouchAssistMapping
                {
                    ToggleSource = XInputBindingSource.DPadDown,
                    DefaultTouched = true
                },
                IndexTouch = new XInputTouchAssistMapping
                {
                    ToggleSource = XInputBindingSource.DPadRight,
                    DefaultTouched = true
                }
            };
        }

        public static bool IsJoystickSource(XInputBindingSource source)
        {
            return source == XInputBindingSource.None ||
                source == XInputBindingSource.LeftStick ||
                source == XInputBindingSource.RightStick;
        }

        public static bool IsButtonOrScalarSource(XInputBindingSource source)
        {
            return Enum.IsDefined(typeof(XInputBindingSource), source) &&
                source != XInputBindingSource.LeftStick &&
                source != XInputBindingSource.RightStick;
        }

        public static bool IsAnalogSource(XInputBindingSource source)
        {
            return source == XInputBindingSource.LeftTrigger ||
                source == XInputBindingSource.RightTrigger;
        }
    }
}
