using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TrackSwap
{
    internal static class WindowThemeService
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        // DWM COLORREF values are 0x00BBGGRR. These neutral tones match App.xaml.
        private const int BorderColor = 0x002C2C2C;
        private const int CaptionColor = 0x001A1A1A;
        private const int TextColor = 0x00D6D6D6;

        internal static void ApplyDarkTitleBar(Window window)
        {
            if (window == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
                }

                // Unsupported color attributes simply return a non-zero HRESULT on older builds.
                int border = BorderColor;
                int caption = CaptionColor;
                int text = TextColor;
                DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
                DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // DWM is unavailable; leave the platform title bar unchanged.
            }
            catch (EntryPointNotFoundException)
            {
                // The platform does not expose this DWM API; leave the title bar unchanged.
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int valueSize);
    }
}
