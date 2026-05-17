using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// SolidWorks expects each <c>CommandGroup</c> to expose a list of PNG icons at fixed pixel
    /// sizes. Icons are generated procedurally (rounded blue tile + "BE") and cached under
    /// <c>%LOCALAPPDATA%\SolidWorksBodyExporter\icons</c>.
    /// </summary>
    internal static class AddInIcons
    {
        private static readonly int[] Sizes = { 16, 20, 32, 40, 48, 64, 96, 128, 256 };

        private static readonly Color Background = Color.FromArgb(255, 31, 90, 165);
        private static readonly Color BorderColor = Color.FromArgb(255, 22, 64, 116);

        /// <summary>Fraction of edge inset (minimal padding so the glyph fills the tile).</summary>
        private const float PadRatio = 0.035f;

        public static IconBundle EnsurePngs()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksBodyExporter",
                "icons");
            Directory.CreateDirectory(dir);

            var paths = new string[Sizes.Length];
            for (var i = 0; i < Sizes.Length; i++)
            {
                var size = Sizes[i];
                var path = Path.Combine(dir, $"BodyExporter_{size}.png");
                CreateIcon(path, size);
                paths[i] = path;
            }

            return new IconBundle(paths);
        }

        private static void CreateIcon(string path, int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = size <= 24 ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
                g.TextRenderingHint = size <= 24
                    ? TextRenderingHint.SingleBitPerPixelGridFit
                    : TextRenderingHint.ClearTypeGridFit;

                var pad = Math.Max(1, (int)Math.Round(size * PadRatio));
                var rect = new Rectangle(pad, pad, size - (pad * 2) - 1, size - (pad * 2) - 1);
                var radius = Math.Max(2, (int)Math.Round(rect.Width * 0.22f));
                var borderW = Math.Max(1f, size / 40f);

                using (var bgPath = RoundedRect(rect, radius))
                using (var bg = new SolidBrush(Background))
                using (var border = new Pen(BorderColor, borderW))
                {
                    g.FillPath(bg, bgPath);
                    g.DrawPath(border, bgPath);
                }

                var fontPx = rect.Height * 0.52f;
                using (var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var pathLetters = new GraphicsPath())
                using (var white = new SolidBrush(Color.White))
                {
                    pathLetters.AddString(
                        "BE",
                        font.FontFamily,
                        (int)font.Style,
                        fontPx,
                        rect,
                        new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center,
                            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
                        });
                    g.FillPath(white, pathLetters);
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            var d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class IconBundle
    {
        public IconBundle(string[] paths)
        {
            Paths = paths;
        }

        public string[] Paths { get; }
    }
}
