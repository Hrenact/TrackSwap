using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TrackSwap.Protocol;

namespace TrackSwap.Controls
{
    public sealed class HapticTimelineIndicator : FrameworkElement
    {
        private const double WindowSeconds = 3.0;
        private IReadOnlyList<OscHapticPreviewSample> samples = Array.Empty<OscHapticPreviewSample>();

        public void SetSamples(IEnumerable<OscHapticPreviewSample> value)
        {
            samples = value?.ToArray() ?? Array.Empty<OscHapticPreviewSample>();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            double width = Math.Max(1.0, ActualWidth);
            double height = Math.Max(1.0, ActualHeight);
            Color signal = ReadColor("MutedTextBrush", Color.FromRgb(146, 146, 146));
            Color background = ReadColor("WindowBrush", Color.FromRgb(18, 18, 18));
            Color border = ReadColor("BorderBrush", Color.FromRgb(44, 44, 44));
            var backgroundBrush = new SolidColorBrush(background);
            var eventBrush = new SolidColorBrush(Color.FromArgb(38, signal.R, signal.G, signal.B));
            var signalPen = new Pen(new SolidColorBrush(signal), 1.0);
            var borderPen = new Pen(new SolidColorBrush(border), 1.0);
            Rect bounds = new Rect(0.5, 0.5, Math.Max(0.0, width - 1.0), Math.Max(0.0, height - 1.0));
            drawingContext.DrawRoundedRectangle(backgroundBrush, borderPen, bounds, 2.0, 2.0);
            drawingContext.PushClip(new RectangleGeometry(bounds, 2.0, 2.0));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset windowStart = now - TimeSpan.FromSeconds(WindowSeconds);
            double pixelsPerSecond = width / WindowSeconds;
            foreach (OscHapticPreviewSample sample in samples)
            {
                double duration = Math.Max(0.02, Math.Min(10.0, sample.DurationSeconds));
                DateTimeOffset eventEnd = sample.OccurredAtUtc + TimeSpan.FromSeconds(duration);
                if (eventEnd < windowStart || sample.OccurredAtUtc > now)
                {
                    continue;
                }

                double left = Math.Max(0.0, (sample.OccurredAtUtc - windowStart).TotalSeconds * pixelsPerSecond);
                double right = Math.Min(width, (eventEnd - windowStart).TotalSeconds * pixelsPerSecond);
                if (right - left < 1.0)
                {
                    right = Math.Min(width, left + 1.0);
                }
                double amplitude = Clamp(sample.Amplitude, 0.0, 1.0);
                double eventHeight = Math.Max(1.0, amplitude * Math.Max(1.0, height - 2.0));
                double top = height - 1.0 - eventHeight;
                Rect eventBounds = new Rect(left, top, Math.Max(1.0, right - left), eventHeight);
                drawingContext.DrawRectangle(eventBrush, null, eventBounds);

                double normalizedFrequency = Math.Log(1.0 + Math.Max(0.0, sample.Frequency)) /
                    Math.Log(321.0);
                double visualFrequency = 2.0 + (14.0 * Clamp(normalizedFrequency, 0.0, 1.0));
                double spacing = Math.Max(2.0, pixelsPerSecond / visualFrequency);
                for (double x = left + 0.5; x <= right; x += spacing)
                {
                    drawingContext.DrawLine(signalPen, new Point(x, top), new Point(x, height - 1.0));
                }
            }

            drawingContext.Pop();
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
