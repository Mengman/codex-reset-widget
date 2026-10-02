using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexResetWidget.Presentation.Views;

// All toolbar and navigation icons share a 16 DIP canvas and 1.5 DIP stroke.
public sealed class VectorIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string),
        typeof(VectorIcon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public VectorIcon() { Width = Height = 16; IsHitTestVisible = false; Focusable = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var pen = new Pen(Foreground, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (Kind == "more")
        { foreach (var x in new[] { 3d, 8d, 13d }) dc.DrawEllipse(Foreground, null, new(x, 8), 1.2, 1.2); return; }
        const string pin = "M4 2h8v2l-1.5 2v4l2 2h-9l2-2V6L4 4z";
        if (Kind is "pin" or "pinned")
        {
            if (Kind == "pin") dc.PushTransform(new RotateTransform(35, 8, 8));
            dc.DrawGeometry(Kind == "pinned" ? Foreground : null, Kind == "pinned" ? null : pen, Geometry.Parse(pin));
            dc.DrawLine(pen, new(8, 12), new(8, 15));
            if (Kind == "pin") dc.Pop();
            return;
        }
        var data = Kind switch
        {
            "refresh" => "M13.2 5.5A5.7 5.7 0 1 0 13.5 10 M13.2 1.8v3.7H9.5",
            "down" => "M2.5 5.5L8 11l5.5-5.5", "up" => "M2.5 10.5L8 5l5.5 5.5",
            "close" => "M3 3l10 10 M13 3L3 13", "left" => "M11.5 2.5L4.5 8l7 5.5",
            "right" => "M4.5 2.5L11.5 8l-7 5.5", _ => ""
        };
        if (data.Length > 0) dc.DrawGeometry(null, pen, Geometry.Parse(data));
    }
}
