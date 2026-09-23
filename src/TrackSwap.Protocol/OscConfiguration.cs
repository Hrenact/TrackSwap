using System;
using System.Collections.Generic;
using System.Linq;

namespace TrackSwap.Protocol
{
    public enum OscResetTimeout
    {
        Never = 0,
        OneSecond = 1,
        FiveSeconds = 5,
        ThirtySeconds = 30,
        OneMinute = 60
    }

    public sealed class OscConfiguration
    {
        public bool Enabled { get; set; }
        public string ListenAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9015;
        public OscResetTimeout ResetTimeout { get; set; } = OscResetTimeout.FiveSeconds;

        public static OscConfiguration CreateDefault()
        {
            return new OscConfiguration();
        }

        public static bool IsRequiredForRoutes(IEnumerable<RouteConfiguration>? routes)
        {
            return routes?.Any(route =>
                route != null &&
                route.Enabled &&
                route.Mode == RouteMode.VirtualController &&
                route.ControlInputSource == ControlInputSource.Osc) == true;
        }
    }

    public sealed class OscControllerAddresses
    {
        private OscControllerAddresses(string hand, string primaryButton, string secondaryButton)
        {
            string prefix = "/trackswap/" + hand + "/";
            PrimaryButton = prefix + "button/" + primaryButton;
            SecondaryButton = prefix + "button/" + secondaryButton;
            JoystickX = prefix + "joystick/x";
            JoystickY = prefix + "joystick/y";
            JoystickClick = prefix + "joystick/click";
            TriggerValue = prefix + "trigger/value";
            GripValue = prefix + "grip/value";
            MenuButton = prefix + "button/menu";
        }

        public string PrimaryButton { get; }
        public string SecondaryButton { get; }
        public string JoystickX { get; }
        public string JoystickY { get; }
        public string JoystickClick { get; }
        public string TriggerValue { get; }
        public string GripValue { get; }
        public string MenuButton { get; }

        public static OscControllerAddresses ForHand(ControllerHand hand)
        {
            if (hand == ControllerHand.Left)
            {
                return new OscControllerAddresses("left", "x", "y");
            }
            if (hand == ControllerHand.Right)
            {
                return new OscControllerAddresses("right", "a", "b");
            }
            throw new ArgumentOutOfRangeException(nameof(hand));
        }
    }

    public sealed class ControllerInputState
    {
        public float JoystickX { get; set; }
        public float JoystickY { get; set; }
        public float TriggerValue { get; set; }
        public float GripValue { get; set; }
        public bool JoystickClick { get; set; }
        public bool PrimaryButton { get; set; }
        public bool SecondaryButton { get; set; }
        public bool MenuButton { get; set; }
        public bool HasExplicitTouchState { get; set; }
        public bool JoystickTouch { get; set; }
        public bool TriggerTouch { get; set; }
        public bool GripTouch { get; set; }
        public bool PrimaryTouch { get; set; }
        public bool SecondaryTouch { get; set; }
        public bool MenuTouch { get; set; }
        public bool ThumbRestTouch { get; set; }
    }
}
