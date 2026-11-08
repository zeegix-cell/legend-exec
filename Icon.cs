using System.Windows;
using System.Windows.Media;

namespace Legend
{
    public class Icon : FrameworkElement
    {
        static readonly Brush DefaultStroke = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E)));
        static Brush Freeze(Brush b) { b.Freeze(); return b; }

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(Geometry), typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(Icon),
            new FrameworkPropertyMetadata(DefaultStroke, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(Icon),
            new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

        public Geometry Data { get => (Geometry)GetValue(DataProperty); set => SetValue(DataProperty, value); }
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

        public Icon()
        {
            Width = 16; Height = 16;
            SnapsToDevicePixels = false;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Data == null) return;
            double s = System.Math.Min(ActualWidth, ActualHeight) / 24.0;
            var pen = new Pen(Stroke, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.PushTransform(new ScaleTransform(s, s));
            dc.DrawGeometry(Fill, pen, Data);
            dc.Pop();
        }
    }
}
