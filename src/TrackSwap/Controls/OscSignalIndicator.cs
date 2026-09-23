using System;
using System.Windows;
using System.Windows.Media;

namespace TrackSwap.Controls
{
    public enum OscSignalIndicatorMode
    {
        Boolean,
        Scalar,
        Joystick,
        IconBoolean,
        IconScalar
    }

    public sealed class OscSignalIndicator : FrameworkElement
    {
        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
            nameof(Mode), typeof(OscSignalIndicatorMode), typeof(OscSignalIndicator),
            new FrameworkPropertyMetadata(OscSignalIndicatorMode.Boolean, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(OscSignalIndicator),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty XProperty = DependencyProperty.Register(
            nameof(X), typeof(double), typeof(OscSignalIndicator),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty YProperty = DependencyProperty.Register(
            nameof(Y), typeof(double), typeof(OscSignalIndicator),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public OscSignalIndicatorMode Mode
        {
            get => (OscSignalIndicatorMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double X
        {
            get => (double)GetValue(XProperty);
            set => SetValue(XProperty, value);
        }

        public double Y
        {
            get => (double)GetValue(YProperty);
            set => SetValue(YProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            Color signal = ReadColor("MutedTextBrush", Color.FromRgb(146, 146, 146));
            Color border = ReadColor("BorderBrush", Color.FromRgb(44, 44, 44));
            var signalBrush = new SolidColorBrush(signal);
            var iconSignalBrush = new SolidColorBrush(Color.FromArgb(96, signal.R, signal.G, signal.B));
            var trackBrush = new SolidColorBrush(Color.FromArgb(110, border.R, border.G, border.B));
            var signalPen = new Pen(signalBrush, 1.0);

            if (Mode == OscSignalIndicatorMode.IconBoolean || Mode == OscSignalIndicatorMode.IconScalar)
            {
                double iconAmount = Mode == OscSignalIndicatorMode.IconBoolean
                    ? (Value > 0.5 ? 1.0 : 0.0)
                    : Clamp(Value, 0.0, 1.0);
                if (iconAmount > 0.0)
                {
                    Rect fill = new Rect(0, 0, Math.Max(0, ActualWidth) * iconAmount, Math.Max(0, ActualHeight));
                    drawingContext.DrawRoundedRectangle(iconSignalBrush, null, fill, 2.0, 2.0);
                }
                return;
            }

            if (Mode == OscSignalIndicatorMode.Joystick)
            {
                double side = Math.Max(1.0, Math.Min(ActualWidth, ActualHeight) - 1.0);
                Rect box = new Rect((ActualWidth - side) / 2.0, (ActualHeight - side) / 2.0, side, side);
                drawingContext.DrawRectangle(trackBrush, null, box);
                double x = box.Left + (Clamp(X, -1.0, 1.0) + 1.0) * box.Width / 2.0;
                double y = box.Top + (1.0 - Clamp(Y, -1.0, 1.0)) * box.Height / 2.0;
                drawingContext.DrawLine(signalPen, new Point(x, box.Top), new Point(x, box.Bottom));
                drawingContext.DrawLine(signalPen, new Point(box.Left, y), new Point(box.Right, y));
                return;
            }

            const double barHeight = 16.0;
            Rect track = new Rect(0, Math.Max(0, (ActualHeight - barHeight) / 2.0), Math.Max(0, ActualWidth), barHeight);
            drawingContext.DrawRoundedRectangle(trackBrush, null, track, 1.0, 1.0);
            double amount = Mode == OscSignalIndicatorMode.Boolean
                ? (Value > 0.5 ? 1.0 : 0.0)
                : Clamp(Value, 0.0, 1.0);
            if (amount > 0.0)
            {
                Rect fill = new Rect(track.Left, track.Top, track.Width * amount, track.Height);
                drawingContext.DrawRoundedRectangle(signalBrush, null, fill, 1.0, 1.0);
            }
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static Color ReadColor(string resourceKey, Color fallback)
        {
            return Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush
                ? brush.Color
                : fallback;
        }
    }
}
