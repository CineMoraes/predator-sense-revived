using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PredatorSenseRevived;

/// <summary>--make-icon out.ico: draws the app logo (angular shield + Y crest, cyan to blue) and writes a multi-size .ico.</summary>
public static class IconMaker
{
    public static void Make(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = sizes.Select(Render).ToList();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    private static byte[] Render(int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double k = size / 34.0;
            dc.PushTransform(new ScaleTransform(k, k));
            // dark angular plate behind the logo (chamfered square)
            var plate = Geometry.Parse("M5,0 L34,0 L34,29 L29,34 L0,34 L0,5 Z");
            dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(0x18, 0x21, 0x3A), Color.FromRgb(0x07, 0x09, 0x0E), 90), null, plate);
            var grad = new LinearGradientBrush(Color.FromRgb(0x00, 0xD2, 0xF0), Color.FromRgb(0x3B, 0x6C, 0xFF), new Point(0, 0), new Point(1, 1));
            double t = size <= 24 ? 3.4 : size <= 48 ? 2.8 : 2.4;
            var pen = new Pen(grad, t) { LineJoin = PenLineJoin.Miter };
            dc.DrawGeometry(null, pen, Geometry.Parse("M17,3 L30,9.5 L30,21 L17,31 L4,21 L4,9.5 Z"));
            dc.DrawGeometry(null, pen, Geometry.Parse("M10.5,13 L17,18.5 L23.5,13 M17,18.5 L17,26"));
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
