using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Renders a small isometric raster preview of a SolidWorks body from a tessellated mesh.
    /// The output is intended to be displayed at ~56px in the bodies grid AND at ~240px in a
    /// hover-zoom tooltip, so we render natively at the larger size and let WPF down-sample
    /// for the thumbnail. Rendering once at the larger size avoids the "blurry then pop-in
    /// to a different image" feel that comes from generating two separate bitmaps.
    /// <para>
    /// Compared with the v0.5.x renderer this implementation
    /// (a) does NOT stroke each tessellation triangle edge - those strokes turned the preview
    ///     into a mesh of diagonal lines the user explicitly called out as noise, and
    /// (b) shades faces with Lambertian lighting against a fixed iso-light direction instead
    ///     of the depth-sum gradient, so flat faces of the same orientation render as a single
    ///     uniform tone (matching how the body looks in the SolidWorks viewport).
    /// </para>
    /// </summary>
    public static class BodyThumbnailRenderer
    {
        /// <summary>
        /// Native render dimensions. The bodies grid displays the bitmap at 56-72px and the
        /// hover tooltip at ~200-240px. Rendering once at <c>NativeSize</c> serves both use
        /// cases - WPF's high-quality bilinear filter handles down-sampling for the grid, and
        /// the tooltip displays the bitmap close to 1:1 so the zoom stays sharp.
        /// </summary>
        private const int NativeSize = 240;
        private const double Padding = 12.0;

        // Standard 30-degree isometric projection.
        private static readonly double IsoCos = Math.Cos(Math.PI / 6.0);
        private static readonly double IsoSin = Math.Sin(Math.PI / 6.0);

        /// <summary>
        /// Direction the virtual light comes FROM in world space (front-up-right of the part,
        /// matching the camera angle). Lambertian shading takes <c>max(0, dot(faceNormal,
        /// lightDir))</c> as the brightness multiplier. Z is positive because the SolidWorks
        /// world Z axis points up by convention and we want the top of the part lit.
        /// </summary>
        private static readonly double[] LightDirection = NormalizeInPlace(new[] { 0.6, -0.4, 0.7 });

        // Technical-drawing style: neutral grayscale fill + dark silhouette (no wood tint).
        private static readonly Color BaseLight = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color BaseDark  = Color.FromRgb(0x4A, 0x4A, 0x4A);
        private static readonly Brush OutlineBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly Pen OutlinePen = new Pen(OutlineBrush, 1.15) { LineJoin = PenLineJoin.Miter };

        static BodyThumbnailRenderer()
        {
            OutlineBrush.Freeze();
            OutlinePen.Freeze();
        }

        public static ImageSource Render(Body2 body)
        {
            try
            {
                var triangles = ExtractTriangles(body);
                if (triangles == null || triangles.Length == 0)
                {
                    return null;
                }

                var projected = ProjectAndCenter(triangles, out var projectionMeta);
                return Rasterize(triangles, projected, projectionMeta);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Pulls the body's facet mesh out of SolidWorks and returns a flat array of triangle
        /// vertex coordinates: each consecutive 9 doubles describe one triangle as
        /// <c>(x0,y0,z0, x1,y1,z1, x2,y2,z2)</c>. The mesh is body-scoped and detached from the
        /// model document - we never mutate the part.
        /// </summary>
        private static double[] ExtractTriangles(Body2 body)
        {
            if (!(body.GetTessellation(null) is Tessellation tess))
            {
                return null;
            }

            tess.NeedFaceFacetMap = false;
            tess.NeedVertexNormal = false;
            tess.NeedVertexParams = false;
            tess.NeedEdgeFinMap = false;
            tess.NeedErrorList = false;
            tess.ImprovedQuality = true; // higher quality is acceptable - we render at 240px native
            tess.MatchType = 0;

            if (!tess.Tessellate())
            {
                return null;
            }

            var facetCount = tess.GetFacetCount();
            if (facetCount <= 0)
            {
                return null;
            }

            var vertexCache = new Dictionary<int, double[]>(facetCount * 3);

            double[] GetVertex(int index)
            {
                if (!vertexCache.TryGetValue(index, out var p))
                {
                    p = (double[])tess.GetVertexPoint(index);
                    vertexCache[index] = p;
                }
                return p;
            }

            var triangles = new double[facetCount * 9];

            for (var f = 0; f < facetCount; f++)
            {
                if (!(tess.GetFacetFins(f) is int[] finIndices) || finIndices.Length < 3)
                {
                    continue;
                }

                var v0Idx = ((int[])tess.GetFinVertices(finIndices[0]))[0];
                var v1Idx = ((int[])tess.GetFinVertices(finIndices[1]))[0];
                var v2Idx = ((int[])tess.GetFinVertices(finIndices[2]))[0];

                var p0 = GetVertex(v0Idx);
                var p1 = GetVertex(v1Idx);
                var p2 = GetVertex(v2Idx);

                var baseIndex = f * 9;
                triangles[baseIndex + 0] = p0[0]; triangles[baseIndex + 1] = p0[1]; triangles[baseIndex + 2] = p0[2];
                triangles[baseIndex + 3] = p1[0]; triangles[baseIndex + 4] = p1[1]; triangles[baseIndex + 5] = p1[2];
                triangles[baseIndex + 6] = p2[0]; triangles[baseIndex + 7] = p2[1]; triangles[baseIndex + 8] = p2[2];
            }

            return triangles;
        }

        /// <summary>
        /// Captures the per-triangle data we need at rasterisation time: 2D pixel coordinates
        /// of each vertex, the average depth (for back-to-front sort), and the 3D face normal
        /// (for Lambertian shading). Returning these together lets <see cref="Rasterize"/> draw
        /// one triangle in a single loop iteration without recomputing the projection.
        /// </summary>
        private struct ProjectionMeta
        {
            public double[] Depths;     // length = triangleCount
            public double[] FaceNormals; // length = triangleCount * 3 (x,y,z per triangle)
        }

        private static double[] ProjectAndCenter(double[] triangles, out ProjectionMeta meta)
        {
            var triangleCount = triangles.Length / 9;
            var projected = new double[triangleCount * 6];
            meta = new ProjectionMeta
            {
                Depths = new double[triangleCount],
                FaceNormals = new double[triangleCount * 3]
            };

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

            for (var t = 0; t < triangleCount; t++)
            {
                var baseInput = t * 9;
                var baseOutput = t * 6;
                double depthSum = 0;

                // First pass: compute the projected (x', y') pair for each of the three
                // triangle vertices and accumulate the world-space depth sum (used for the
                // painter's-algorithm sort).
                for (var v = 0; v < 3; v++)
                {
                    var x = triangles[baseInput + v * 3];
                    var y = triangles[baseInput + v * 3 + 1];
                    var z = triangles[baseInput + v * 3 + 2];

                    var px = (x - y) * IsoCos;
                    var py = (x + y) * IsoSin - z;

                    projected[baseOutput + v * 2] = px;
                    projected[baseOutput + v * 2 + 1] = py;

                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;

                    depthSum += x + y + z;
                }

                meta.Depths[t] = depthSum;

                // Compute the un-normalised face normal once. The cross product of two edge
                // vectors of the triangle gives a vector perpendicular to the face; its sign
                // is taken to point "away from the part" by relying on SolidWorks' counter-
                // clockwise winding convention for outward-facing facets. Lambertian shading
                // takes the absolute value of the dot with the light, so even bodies whose
                // winding is reversed render with sensible brightness.
                var ax = triangles[baseInput + 3] - triangles[baseInput + 0];
                var ay = triangles[baseInput + 4] - triangles[baseInput + 1];
                var az = triangles[baseInput + 5] - triangles[baseInput + 2];
                var bx = triangles[baseInput + 6] - triangles[baseInput + 0];
                var by = triangles[baseInput + 7] - triangles[baseInput + 1];
                var bz = triangles[baseInput + 8] - triangles[baseInput + 2];

                var nx = ay * bz - az * by;
                var ny = az * bx - ax * bz;
                var nz = ax * by - ay * bx;
                var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-12)
                {
                    nx /= len; ny /= len; nz /= len;
                }

                var normalBase = t * 3;
                meta.FaceNormals[normalBase + 0] = nx;
                meta.FaceNormals[normalBase + 1] = ny;
                meta.FaceNormals[normalBase + 2] = nz;
            }

            var spanX = Math.Max(maxX - minX, 1e-9);
            var spanY = Math.Max(maxY - minY, 1e-9);
            var scale = Math.Min((NativeSize - 2 * Padding) / spanX, (NativeSize - 2 * Padding) / spanY);

            var offsetX = (NativeSize - spanX * scale) / 2.0 - minX * scale;
            var offsetY = (NativeSize - spanY * scale) / 2.0 + maxY * scale;

            for (var i = 0; i < projected.Length; i += 2)
            {
                projected[i] = projected[i] * scale + offsetX;
                projected[i + 1] = -projected[i + 1] * scale + offsetY;
            }

            return projected;
        }

        private static ImageSource Rasterize(double[] triangles, double[] projected, ProjectionMeta meta)
        {
            var triangleCount = meta.Depths.Length;
            var order = Enumerable.Range(0, triangleCount).OrderBy(i => meta.Depths[i]).ToArray();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Transparent background so the parent Border's beige tile shows through. We
                // rely on per-triangle fill alpha being 1.0 so the silhouette is opaque while
                // any unrendered margin stays transparent.
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, NativeSize, NativeSize));

                foreach (var t in order)
                {
                    var baseOutput = t * 6;
                    var p1 = new Point(projected[baseOutput], projected[baseOutput + 1]);
                    var p2 = new Point(projected[baseOutput + 2], projected[baseOutput + 3]);
                    var p3 = new Point(projected[baseOutput + 4], projected[baseOutput + 5]);

                    var brush = LambertianBrush(meta, t);

                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(p1, true, true);
                        ctx.LineTo(p2, true, false);
                        ctx.LineTo(p3, true, false);
                    }
                    geometry.Freeze();

                    // Note: NO per-triangle stroke pen. The previous renderer drew an edge pen
                    // around every facet, which manifested as the diagonal mesh lines the user
                    // complained about. We trade those edges for a single convex-hull outline
                    // pass below so the silhouette still reads as a solid object rather than a
                    // shaded blob.
                    dc.DrawGeometry(brush, null, geometry);
                }

                DrawTechnicalEdges(dc, triangles, projected, meta, triangleCount);
            }

            var bitmap = new RenderTargetBitmap(NativeSize, NativeSize, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Computes the Lambertian-shaded fill colour for a single triangle. The face normal
        /// dotted with the light direction (clamped to <c>[0, 1]</c> via abs) gives a 0..1
        /// brightness scalar; we then lerp between the dark and light anchor colours.
        /// </summary>
        private static SolidColorBrush LambertianBrush(ProjectionMeta meta, int triangleIndex)
        {
            var b = triangleIndex * 3;
            var nx = meta.FaceNormals[b + 0];
            var ny = meta.FaceNormals[b + 1];
            var nz = meta.FaceNormals[b + 2];

            var dot = nx * LightDirection[0] + ny * LightDirection[1] + nz * LightDirection[2];
            // abs() instead of clamp(0, 1) so reverse-winding facets still light up. Add a
            // small ambient floor so faces perpendicular to the light don't go pitch black.
            var lightness = 0.18 + 0.82 * Math.Min(1.0, Math.Abs(dot));

            var r = (byte)Math.Round(BaseDark.R + (BaseLight.R - BaseDark.R) * lightness);
            var g = (byte)Math.Round(BaseDark.G + (BaseLight.G - BaseDark.G) * lightness);
            var bl = (byte)Math.Round(BaseDark.B + (BaseLight.B - BaseDark.B) * lightness);
            var y = (byte)Math.Round(0.2126 * r + 0.7152 * g + 0.0722 * bl);
            var brush = new SolidColorBrush(Color.FromRgb(y, y, y));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Visible silhouette + sharp creases in 2D projection (no hidden lines): draws edges
        /// where only one adjacent face exists, where the two faces straddle the view direction,
        /// or where the dihedral angle exceeds a threshold. Uses a fixed orthographic view
        /// vector aligned with the isometric depth used for triangle sorting.
        /// </summary>
        private static void DrawTechnicalEdges(
            DrawingContext dc,
            double[] triangles,
            double[] projected,
            ProjectionMeta meta,
            int triangleCount)
        {
            // View direction in model space — same octant as the depth heuristic used in projection.
            var vx = 0.5773502691896258;
            var vy = 0.5773502691896258;
            var vz = 0.5773502691896258;

            const double silhouetteEps = 0.04;
            const double creaseCos = 0.64; // ~50°

            var edgeMap = new Dictionary<Edge2Key, Edge2Info>(triangleCount * 2);

            void AddEdge(long qx0, long qy0, long qx1, long qy1, int triIdx)
            {
                var key = new Edge2Key(qx0, qy0, qx1, qy1);
                if (!edgeMap.TryGetValue(key, out var info))
                {
                    info = new Edge2Info();
                }

                if (info.Count == 0)
                {
                    info.Tri0 = triIdx;
                    info.Count = 1;
                }
                else if (info.Count == 1 && info.Tri0 != triIdx)
                {
                    info.Tri1 = triIdx;
                    info.Count = 2;
                }

                edgeMap[key] = info;
            }

            long Q(double v) => (long)Math.Round(v * 48.0);

            for (var t = 0; t < triangleCount; t++)
            {
                var b6 = t * 6;
                var p0 = new Point(projected[b6], projected[b6 + 1]);
                var p1 = new Point(projected[b6 + 2], projected[b6 + 3]);
                var p2 = new Point(projected[b6 + 4], projected[b6 + 5]);

                var q0x = Q(p0.X); var q0y = Q(p0.Y);
                var q1x = Q(p1.X); var q1y = Q(p1.Y);
                var q2x = Q(p2.X); var q2y = Q(p2.Y);

                AddEdge(q0x, q0y, q1x, q1y, t);
                AddEdge(q1x, q1y, q2x, q2y, t);
                AddEdge(q2x, q2y, q0x, q0y, t);
            }

            double FaceDot(int tri)
            {
                var b = tri * 3;
                return meta.FaceNormals[b] * vx + meta.FaceNormals[b + 1] * vy + meta.FaceNormals[b + 2] * vz;
            }

            static double DotNormals(ProjectionMeta m, int t1, int t2)
            {
                var b1 = t1 * 3;
                var b2 = t2 * 3;
                return m.FaceNormals[b1] * m.FaceNormals[b2]
                    + m.FaceNormals[b1 + 1] * m.FaceNormals[b2 + 1]
                    + m.FaceNormals[b1 + 2] * m.FaceNormals[b2 + 2];
            }

            foreach (var kv in edgeMap)
            {
                var key = kv.Key;
                var info = kv.Value;
                var p0 = new Point(key.X0 / 48.0, key.Y0 / 48.0);
                var p1 = new Point(key.X1 / 48.0, key.Y1 / 48.0);

                bool draw;
                if (info.Count == 1)
                {
                    var d0 = FaceDot(info.Tri0);
                    draw = d0 > -silhouetteEps;
                }
                else
                {
                    var d0 = FaceDot(info.Tri0);
                    var d1 = FaceDot(info.Tri1);
                    const double silhouetteDot = 0.002;
                    var silhouette = d0 * d1 <= silhouetteDot;
                    var crease = DotNormals(meta, info.Tri0, info.Tri1) < creaseCos;
                    var vis0 = d0 > -silhouetteEps;
                    var vis1 = d1 > -silhouetteEps;
                    draw = (vis0 || vis1) && (silhouette || crease);
                }

                if (!draw) continue;

                dc.DrawLine(OutlinePen, p0, p1);
            }
        }

        private readonly struct Edge2Key : IEquatable<Edge2Key>
        {
            public readonly long X0;
            public readonly long Y0;
            public readonly long X1;
            public readonly long Y1;

            public Edge2Key(long ax, long ay, long bx, long by)
            {
                if (ax < bx || (ax == bx && ay < by))
                {
                    X0 = ax; Y0 = ay; X1 = bx; Y1 = by;
                }
                else
                {
                    X0 = bx; Y0 = by; X1 = ax; Y1 = ay;
                }
            }

            public bool Equals(Edge2Key other) =>
                X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1;

            public override bool Equals(object obj) => obj is Edge2Key other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var h = (int)2166136261;
                    h = (h * 16777619) ^ X0.GetHashCode();
                    h = (h * 16777619) ^ Y0.GetHashCode();
                    h = (h * 16777619) ^ X1.GetHashCode();
                    h = (h * 16777619) ^ Y1.GetHashCode();
                    return h;
                }
            }
        }

        private struct Edge2Info
        {
            public int Tri0;
            public int Tri1;
            public int Count;
        }

        private static double[] NormalizeInPlace(double[] v)
        {
            var len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (len > 1e-12)
            {
                v[0] /= len; v[1] /= len; v[2] /= len;
            }
            return v;
        }
    }
}
