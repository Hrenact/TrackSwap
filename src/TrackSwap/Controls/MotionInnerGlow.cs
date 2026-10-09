using System;
using System.Windows;
using System.Windows.Media;

namespace TrackSwap.Controls
{
    public sealed class MotionInnerGlow : FrameworkElement
    {
        public static readonly DependencyProperty IntensityProperty =
            DependencyProperty.Register(
                nameof(Intensity),
                typeof(double),
                typeof(MotionInnerGlow),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public MotionInnerGlow()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
        }

        public double Intensity
        {
            get => (double)GetValue(IntensityProperty);
            set => SetValue(IntensityProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            double intensity = Math.Max(0.0, Math.Min(1.0, Intensity));
            if (intensity <= 0.001 || ActualWidth <= 1.0 || ActualHeight <= 1.0)
            {
                return;
            }

            Color accent = Application.Current?.TryFindResource("AccentBrush") is SolidColorBrush brush
                ? brush.Color
                : Color.FromRgb(90, 155, 255);
            Rect clipBounds = new Rect(0.0, 0.0, ActualWidth, ActualHeight);
            drawingContext.PushClip(new RectangleGeometry(clipBounds, 2.0, 2.0));
            const int layerCount = 7;
            for (int layer = 0; layer < layerCount; layer++)
            {
                double progress = layer / (double)(layerCount - 1);
                double inset = 0.5 + (layer * 1.35);
                double width = ActualWidth - (inset * 2.0);
                double height = ActualHeight - (inset * 2.0);
                if (width <= 0.0 || height <= 0.0)
                {
                    break;
                }
                double alpha = intensity * 0.34 * Math.Pow(1.0 - progress, 1.7);
                var layerBrush = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Round(255.0 * alpha),
                    accent.R,
                    accent.G,
                    accent.B));
                layerBrush.Freeze();
                var pen = new Pen(layerBrush, layer == 0 ? 1.5 : 1.0);
                pen.Freeze();
                drawingContext.DrawRoundedRectangle(
                    null,
                    pen,
                    new Rect(inset, inset, width, height),
                    Math.Max(0.0, 2.0 - (progress * 1.5)),
                    Math.Max(0.0, 2.0 - (progress * 1.5)));
            }
            drawingContext.Pop();
        }
    }
}
