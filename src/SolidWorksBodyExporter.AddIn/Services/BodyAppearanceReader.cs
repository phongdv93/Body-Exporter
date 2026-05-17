using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Per-body appearance information sourced from the SolidWorks RenderMaterial collection. The
    /// "appearance" here is the visual paint/texture on the body (e.g. "Stained Ash") as opposed to
    /// the engineering material (e.g. "Ash Wood") which is read separately by <see cref="BodyScanner"/>.
    /// </summary>
    public sealed class BodyAppearance
    {
        public string TextureName { get; set; }
        public string ColorName { get; set; }
        public string ColorHex { get; set; }
    }

    /// <summary>
    /// Walks the model's render materials once and indexes them by the body name they target so the
    /// per-body lookups during scanning stay O(1).
    /// </summary>
    public sealed class BodyAppearanceReader
    {
        private static readonly (string Name, byte R, byte G, byte B)[] Palette =
        {
            ("Black", 0, 0, 0),
            ("White", 255, 255, 255),
            ("Light Gray", 211, 211, 211),
            ("Gray", 128, 128, 128),
            ("Dark Gray", 64, 64, 64),
            ("Silver", 192, 192, 192),
            ("Red", 220, 20, 60),
            ("Dark Red", 139, 0, 0),
            ("Pink", 255, 192, 203),
            ("Orange", 255, 140, 0),
            ("Coral", 255, 127, 80),
            ("Salmon", 250, 128, 114),
            ("Yellow", 255, 215, 0),
            ("Gold", 218, 165, 32),
            ("Brown", 139, 69, 19),
            ("Sienna", 160, 82, 45),
            ("Tan", 210, 180, 140),
            ("Beige", 222, 184, 135),
            ("Burlywood", 222, 184, 135),
            ("Wheat", 245, 222, 179),
            ("Khaki", 240, 230, 140),
            ("Olive", 128, 128, 0),
            ("Dark Olive", 85, 107, 47),
            ("Green", 0, 128, 0),
            ("Dark Green", 0, 100, 0),
            ("Forest Green", 34, 139, 34),
            ("Lime", 50, 205, 50),
            ("Cyan", 0, 200, 200),
            ("Teal", 0, 128, 128),
            ("Turquoise", 64, 224, 208),
            ("Blue", 30, 144, 255),
            ("Navy", 0, 0, 128),
            ("Royal Blue", 65, 105, 225),
            ("Sky Blue", 135, 206, 235),
            ("Purple", 128, 0, 128),
            ("Violet", 138, 43, 226),
            ("Magenta", 255, 0, 255),
            ("Indigo", 75, 0, 130)
        };

        private readonly Dictionary<string, BodyAppearance> _byBodyName =
            new Dictionary<string, BodyAppearance>(StringComparer.OrdinalIgnoreCase);

        private BodyAppearance _partLevelFallback;

        public BodyAppearanceReader(ModelDoc2 model)
        {
            if (model == null)
            {
                return;
            }

            try
            {
                BuildFromRenderMaterials(model);
            }
            catch
            {
                // RenderMaterial APIs are version sensitive. If anything throws we still want body
                // scanning to succeed - the lookups will simply return null for unknown bodies.
            }
        }

        public BodyAppearance Read(Body2 body)
        {
            if (body == null)
            {
                return _partLevelFallback;
            }

            if (_byBodyName.TryGetValue(body.Name, out var appearance))
            {
                return appearance;
            }

            // Fall back to body's own material RGB values - this catches the simple "Color" assignment
            // that does not create a full RenderMaterial entry.
            var rgb = TryReadBodyColor(body);
            if (rgb.HasValue)
            {
                var named = NearestNamedColor(rgb.Value);
                return new BodyAppearance
                {
                    TextureName = null,
                    ColorName = named,
                    ColorHex = FormatHex(rgb.Value)
                };
            }

            return _partLevelFallback;
        }

        private void BuildFromRenderMaterials(ModelDoc2 model)
        {
            var renderMaterials = model.Extension.GetRenderMaterials2(
                (int)swDisplayStateOpts_e.swThisDisplayState,
                null) as object[];

            if (renderMaterials == null || renderMaterials.Length == 0)
            {
                return;
            }

            foreach (var rmObj in renderMaterials)
            {
                if (!(rmObj is RenderMaterial rm))
                {
                    continue;
                }

                var appearance = BuildAppearance(rm);
                if (appearance == null)
                {
                    continue;
                }

                if (rm.GetEntities() is object[] entities)
                {
                    var matched = false;
                    foreach (var entity in entities)
                    {
                        if (entity is Body2 body && !string.IsNullOrEmpty(body.Name))
                        {
                            _byBodyName[body.Name] = appearance;
                            matched = true;
                        }
                        else if (entity is Feature feature && feature.GetBody() is Body2 featureBody &&
                                 !string.IsNullOrEmpty(featureBody.Name))
                        {
                            _byBodyName[featureBody.Name] = appearance;
                            matched = true;
                        }
                    }

                    if (!matched)
                    {
                        // Entities exist but none mapped to a body (could be face- or part-level only).
                        // Keep this appearance as a part-wide fallback for bodies without a specific
                        // entry of their own.
                        _partLevelFallback ??= appearance;
                    }
                }
                else
                {
                    _partLevelFallback ??= appearance;
                }
            }
        }

        private static BodyAppearance BuildAppearance(RenderMaterial rm)
        {
            string textureName = null;
            try
            {
                textureName = ExtractTextureName(rm.FileName);
            }
            catch
            {
            }

            (byte R, byte G, byte B)? rgb = null;
            try
            {
                rgb = ParseSolidWorksColor(rm.PrimaryColor);
            }
            catch
            {
            }

            if (textureName == null && rgb == null)
            {
                return null;
            }

            return new BodyAppearance
            {
                TextureName = textureName,
                ColorName = rgb.HasValue ? NearestNamedColor(rgb.Value) : null,
                ColorHex = rgb.HasValue ? FormatHex(rgb.Value) : null
            };
        }

        private static string ExtractTextureName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var bare = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(bare))
            {
                return null;
            }

            bare = bare.Replace('_', ' ').Replace('-', ' ').Trim();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(bare.ToLowerInvariant());
        }

        private static (byte R, byte G, byte B)? ParseSolidWorksColor(int packed)
        {
            if (packed == 0)
            {
                return null;
            }

            // SolidWorks stores colour as 0x00BBGGRR.
            var r = (byte)(packed & 0xFF);
            var g = (byte)((packed >> 8) & 0xFF);
            var b = (byte)((packed >> 16) & 0xFF);
            return (r, g, b);
        }

        private static (byte R, byte G, byte B)? TryReadBodyColor(Body2 body)
        {
            try
            {
                if (!(body.MaterialPropertyValues2 is double[] values) || values.Length < 3)
                {
                    return null;
                }

                byte ToByte(double v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255)));
                return (ToByte(values[0]), ToByte(values[1]), ToByte(values[2]));
            }
            catch
            {
                return null;
            }
        }

        private static string NearestNamedColor((byte R, byte G, byte B) rgb)
        {
            string best = null;
            var bestDistance = int.MaxValue;
            foreach (var entry in Palette)
            {
                var dr = rgb.R - entry.R;
                var dg = rgb.G - entry.G;
                var db = rgb.B - entry.B;
                var distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = entry.Name;
                }
            }
            return best;
        }

        private static string FormatHex((byte R, byte G, byte B) rgb)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", rgb.R, rgb.G, rgb.B);
        }
    }
}
