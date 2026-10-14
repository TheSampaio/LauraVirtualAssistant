using Laura.Core.Screenshots;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using XLine = Microsoft.UI.Xaml.Shapes.Line;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Laura.App.Platforms.Windows;

/// <summary>One image-coordinate renderer used by the editor and original-resolution export.</summary>
internal static class ScreenshotRendering
{
    internal static WriteableBitmap Bitmap(ScreenshotFrame frame)
    {
        var bitmap = new WriteableBitmap(frame.Bounds.Width, frame.Bounds.Height);
        using var pixels = bitmap.PixelBuffer.AsStream(); pixels.Write(frame.Pixels.Span); return bitmap;
    }
    internal static SolidColorBrush Color(uint value) => new(global::Windows.UI.Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value));
    internal static void Annotation(XCanvas canvas, ScreenshotAnnotation annotation)
    {
        ScreenshotPoint first = annotation.Points[0], last = annotation.Points[^1]; var box = annotation.Box();
        var brush = Color(annotation.Color); double thickness = annotation.Thickness;
        if (annotation.Tool is ScreenshotTool.Line or ScreenshotTool.Arrow)
        {
            Line(first, last);
            if (annotation.Tool == ScreenshotTool.Arrow)
            {
                double angle = Math.Atan2(last.Y - first.Y, last.X - first.X), length = Math.Max(12, thickness * 4);
                Line(last, new(last.X - length * Math.Cos(angle - .5), last.Y - length * Math.Sin(angle - .5)));
                Line(last, new(last.X - length * Math.Cos(angle + .5), last.Y - length * Math.Sin(angle + .5)));
            }
        }
        else if (annotation.Tool is ScreenshotTool.Pen or ScreenshotTool.Highlight)
        {
            var path = new Microsoft.UI.Xaml.Shapes.Polyline { Stroke = brush, StrokeThickness = annotation.Tool == ScreenshotTool.Highlight ? thickness * 5 : thickness,
                Opacity = annotation.Tool == ScreenshotTool.Highlight ? .35 : 1, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            foreach (var point in annotation.Points) { path.Points.Add(new(point.X, point.Y)); } canvas.Children.Add(path);
        }
        else if (annotation.Tool == ScreenshotTool.Text)
        {
            var label = new TextBlock { Text = annotation.Text, FontSize = annotation.FontSize, Foreground = brush, TextWrapping = TextWrapping.NoWrap };
            XCanvas.SetLeft(label, first.X); XCanvas.SetTop(label, first.Y); canvas.Children.Add(label);
        }
        else
        {
            Microsoft.UI.Xaml.Shapes.Shape shape = annotation.Tool == ScreenshotTool.Ellipse ? new Microsoft.UI.Xaml.Shapes.Ellipse() : new Microsoft.UI.Xaml.Shapes.Rectangle();
            shape.Width = box.Width; shape.Height = box.Height; shape.Stroke = brush; shape.StrokeThickness = thickness;
            XCanvas.SetLeft(shape, box.X); XCanvas.SetTop(shape, box.Y); canvas.Children.Add(shape);
        }
        void Line(ScreenshotPoint from, ScreenshotPoint to) => canvas.Children.Add(new XLine { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
    }
}
