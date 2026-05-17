using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// SolidWorks expects each <c>CommandGroup</c> to expose a list of PNG icons at fixed pixel
    /// sizes (20, 32, 40, 64, 96, 128). Without a valid icon list assigned, recent SolidWorks
    /// versions silently drop the command from the Tools menu and CommandManager ribbon even
    /// when <see cref="SolidWorks.Interop.sldworks.ICommandGroup.Activate"/> reports success.
    /// <para>
    /// To keep the add-in self-contained we generate the icons procedurally at runtime and cache
    /// them under <c>%LOCALAPPDATA%\SolidWorksBodyExporter\icons</c>. The shape is a rounded blue
    /// square with the letters "BE" - just enough to be visually distinct in the ribbon.
    /// </para>
    /// </summary>
    internal static class AddInIcons
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        public static IconBundle EnsurePngs()
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksBodyExporter",
                "icons");
            Directory.CreateDirectory(dir);

            var paths = new string[Sizes.Length];
            for (var i = 0; i < Sizes.Length; i++)
            {
                var size = Sizes[i];
                var path = System.IO.Path.Combine(dir, $"BodyExporter_{size}.png");

                // Always regenerate to pick up icon design changes between releases. The files are
                // tiny (a few KB total) so the cost is negligible compared to the troubleshooting
                // overhead of a stale icon hiding a code change.
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
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                var pad = Math.Max(1, size / 10);
                var rect = new Rectangle(pad, pad, size - 2 * pad - 1, size - 2 * pad - 1);
                var radius = Math.Max(2, size / 6);

                using (var bg = new SolidBrush(Color.FromArgb(255, 31, 90, 165)))
                using (var border = new Pen(Color.FromArgb(255, 22, 64, 116), Math.Max(1f, size / 32f)))
                using (var bgPath = RoundedRect(rect, radius))
                {
                    g.FillPath(bg, bgPath);
                    g.DrawPath(border, bgPath);
                }

                var fontSize = size * 0.42f;
                using (var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString("BE", font, Brushes.White, rect, sf);
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

        /// <summary>Paths in ascending pixel size order (20, 32, 40, 64, 96, 128).</summary>
        public string[] Paths { get; }
    }
}
