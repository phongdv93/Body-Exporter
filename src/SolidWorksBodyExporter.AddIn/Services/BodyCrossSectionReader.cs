using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Measures the stock cross-section of a body by cutting it at many stations along its own
    /// length and keeping the section that repeats the most.
    ///
    /// <para>
    /// The bounding box fails on curved parts in both directions at once. A slat bowed along its
    /// length reads wider than its board because the box spans the arc rise; a panel curved across
    /// its width reads narrower, because the box measures the chord while the board was cut flat.
    /// A cross-section suffers from neither: its area is <c>width × thickness</c> whether or not
    /// the part is bent, and dividing a curved section's area by the thickness gives the developed
    /// (flattened) width, which is the board the part was actually cut from.
    /// </para>
    /// <para>
    /// Cuts are taken in the part's own frame, never along X/Y/Z. Most bodies in an assembly sit
    /// at an angle to the model axes, and a cut that is not square to the part slices a longer
    /// section than the real one — that alone reported a 160 mm slat as 182 mm and gave two
    /// mirrored copies of the same slat different widths. The length direction therefore comes
    /// from the mesh's principal axes, and each cut is then squared up against the local direction
    /// of the part's spine so that a body curving along its length is still cut across it.
    /// </para>
    /// <para>
    /// Local features — a notch, a boss, a rounded end — move only a few stations, so the section
    /// shared by the largest group of stations is the stock section.
    /// </para>
    /// </summary>
    internal static class BodyCrossSectionReader
    {
        private const int StationCount = 64;

        /// <summary>Stations skipped at each end, where a cut catches a rounded or mitred face.</summary>
        private const int EdgeStationsSkipped = 3;

        /// <summary>Stations whose areas sit within this band are the same section.</summary>
        private const double ClusterTolerance = 0.02;

        /// <summary>The stock section must repeat over at least this share of the body.</summary>
        private const double MinClusterShare = 0.25;

        /// <summary>A spine straying less than this share of the length is treated as straight.</summary>
        private const double StraightSpineTolerance = 0.005;

        /// <summary>How many times the length axis may be refit before settling.</summary>
        private const int MaxAxisRefinements = 6;

        /// <summary>Refitting stops once the axis moves less than about a twentieth of a degree.</summary>
        private const double AxisSettledDot = 0.9999996;

        /// <summary>Sections swept along the length must reproduce the solid volume this closely.</summary>
        private const double MaxVolumeDeviation = 0.12;

        public sealed class Result
        {
            public Result(
                double areaMm2,
                double perimeterMm,
                double lengthMm,
                double[] lengthAxis,
                bool spineIsStraight,
                int clusterStations,
                int totalStations)
            {
                AreaMm2 = areaMm2;
                PerimeterMm = perimeterMm;
                LengthMm = lengthMm;
                LengthAxis = lengthAxis;
                SpineIsStraight = spineIsStraight;
                ClusterStations = clusterStations;
                TotalStations = totalStations;
            }

            /// <summary>Area of the section that repeats along the body.</summary>
            public double AreaMm2 { get; }

            public double PerimeterMm { get; }

            /// <summary>Extent along the part's own length direction, not along a model axis.</summary>
            public double LengthMm { get; }

            /// <summary>Unit length direction in model coordinates.</summary>
            public double[] LengthAxis { get; }

            /// <summary>
            /// True when the part runs straight. Only then is <see cref="LengthMm"/> the length of
            /// a board it could be cut from — a curved part develops longer than it measures.
            /// </summary>
            public bool SpineIsStraight { get; }

            /// <summary>How many stations shared this section, out of how many were cut.</summary>
            public int ClusterStations { get; }

            public int TotalStations { get; }
        }

        /// <param name="volumeMm3">Solid volume; sections are swept along the length to verify it.</param>
        public static Result TryMeasure(Body2 body, string label, double? volumeMm3)
        {
            if (body == null)
            {
                return null;
            }

            double[] mesh;
            try
            {
                mesh = ExtractTrianglesMm(body);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyCrossSectionReader.mesh: " + ex.Message);
                return null;
            }

            if (mesh == null || mesh.Length < 9)
            {
                return null;
            }

            var frame = TryBuildPartFrame(mesh);
            if (frame == null)
            {
                return null;
            }

            // The principal axes of a body are pulled off the part's true length by whatever is cut
            // into it — a chamfered end skewed them enough to measure a 100 mm block as 111. So the
            // axis is refit to the spine of the stretch that holds a constant section, which is the
            // only part that genuinely runs along the body, and the refit repeats until it settles.
            // Of the axes tried we keep the one giving the smallest section, because a cut that is
            // not square to the part can only ever slice a larger section than the real one. That
            // also makes the answer independent of how the body happens to be placed, so identical
            // parts stop landing on different sizes.
            Attempt best = null;

            for (var round = 0; round < MaxAxisRefinements; round++)
            {
                var attempt = Measure(mesh, frame);
                if (attempt == null)
                {
                    break;
                }

                if (attempt.Band.Count >= StationCount * MinClusterShare
                    && (best == null || attempt.BandArea < best.BandArea))
                {
                    best = attempt;
                }

                if (!TryRefineLengthAxis(attempt, frame, out var refined))
                {
                    break;
                }

                frame = refined;
            }

            if (best == null)
            {
                DiagnosticLog.Info(
                    "BodyCrossSectionReader " + label + ": no stretch of constant section, rejected");
                return null;
            }

            if (volumeMm3.HasValue && volumeMm3.Value > 0)
            {
                var deviation = Math.Abs(best.SweptVolume - volumeMm3.Value) / volumeMm3.Value;
                if (deviation > MaxVolumeDeviation)
                {
                    DiagnosticLog.Info(
                        "BodyCrossSectionReader " + label + ": swept volume " + Fmt(best.SweptVolume)
                        + " does not match solid " + Fmt(volumeMm3.Value) + ", sections rejected");
                    return null;
                }
            }

            var straight = SpineStray(best.Spine, best.Band) <= best.Length * StraightSpineTolerance;
            var squared = SliceAlongSpine(best.Local, best.Spine);
            return PickDominantSection(
                squared, best.Length, best.Along, straight, label, best.SweptVolume);
        }

        /// <summary>One candidate length axis, cut into stations and scored by its section.</summary>
        private sealed class Attempt
        {
            public double[] Local;

            /// <summary>Length direction in model coordinates, for callers measuring edges.</summary>
            public double[] Along;

            public Spine[] Spine;
            public Band Band;
            public double Length;
            public double SweptVolume;

            /// <summary>Typical section over the constant stretch — the score to minimise.</summary>
            public double BandArea;
        }

        private static Attempt Measure(double[] mesh, double[][] frame)
        {
            var local = ToFrame(mesh, frame);
            Extent(local, 2, out var low, out var high);
            var length = high - low;
            if (length <= 0)
            {
                return null;
            }

            var step = length / StationCount;
            var spine = new Spine[StationCount];
            var sweptVolume = 0.0;

            for (var i = 0; i < StationCount; i++)
            {
                var station = low + (i + 0.5) * step;
                var origin = new[] { 0.0, 0.0, station };
                var cut = Slice(local, origin, UnitZ, true);
                spine[i] = new Spine(cut.CentroidU, cut.CentroidV, station, cut.Area);
                sweptVolume += cut.Area * step;
            }

            var band = FindConstantSectionRun(spine);

            return new Attempt
            {
                Local = local,
                Along = frame[2],
                Spine = spine,
                Band = band,
                Length = length,
                SweptVolume = sweptVolume,
                BandArea = MedianBandArea(spine, band)
            };
        }

        private static double MedianBandArea(Spine[] spine, Band band)
        {
            if (band.IsEmpty)
            {
                return double.MaxValue;
            }

            var areas = new List<double>(band.Count);
            for (var i = band.Start; i <= band.End; i++)
            {
                areas.Add(spine[i].Area);
            }

            areas.Sort();
            return areas[areas.Count / 2];
        }

        /// <summary>Range of stations, in order along the part, that share one section.</summary>
        private struct Band
        {
            public int Start;
            public int End;

            public int Count => End - Start + 1;
            public bool IsEmpty => End < Start;
        }

        /// <summary>
        /// Longest unbroken stretch of stations whose sections match. Chamfers, notches and rounded
        /// ends fall outside it, which makes it the stretch that describes the stock.
        /// </summary>
        private static Band FindConstantSectionRun(Spine[] spine)
        {
            var best = new Band { Start = 0, End = -1 };
            var bestCount = 0;

            for (var start = 0; start < spine.Length; start++)
            {
                if (spine[start].Area <= 0)
                {
                    continue;
                }

                var end = start;
                while (end + 1 < spine.Length
                       && spine[end + 1].Area > 0
                       && WithinTolerance(spine[start].Area, spine[end + 1].Area))
                {
                    end++;
                }

                var count = end - start + 1;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = new Band { Start = start, End = end };
                }

                start = end;
            }

            return best;
        }

        private static bool WithinTolerance(double reference, double value)
        {
            var high = Math.Max(reference, value);
            var low = Math.Min(reference, value);
            return high <= low * (1.0 + ClusterTolerance);
        }

        /// <summary>
        /// Aims the length axis along the spine of the constant-section stretch. Only a straight
        /// stretch is used: a curved part has no single length direction, and its own bend would
        /// otherwise be mistaken for a misaligned axis.
        /// </summary>
        private static bool TryRefineLengthAxis(Attempt attempt, double[][] frame, out double[][] refined)
        {
            refined = null;
            var spine = attempt.Spine;
            var band = attempt.Band;
            if (band.IsEmpty || band.Count < 4)
            {
                return false;
            }

            // A curved band is still worth refitting: the least-squares direction is then the
            // chord, which is the axis we want anyway. Refusing to refit here left two copies of
            // one 100 mm block stuck on a 15-degree error, reading 111 mm.
            var span = Math.Abs(spine[band.End].S - spine[band.Start].S);
            if (span <= 0)
            {
                return false;
            }

            if (!TryFitSpineDirection(spine, band, out var run))
            {
                return false;
            }

            if (run[2] > AxisSettledDot)
            {
                return false;
            }

            var along = new double[3];
            for (var c = 0; c < 3; c++)
            {
                along[c] = run[0] * frame[0][c] + run[1] * frame[1][c] + run[2] * frame[2][c];
            }

            if (!TryNormalize(along))
            {
                return false;
            }

            var across = Cross(frame[1], along);
            if (!TryNormalize(across))
            {
                across = Cross(frame[0], along);
                if (!TryNormalize(across))
                {
                    return false;
                }
            }

            var third = Cross(along, across);
            if (!TryNormalize(third))
            {
                return false;
            }

            refined = new[] { across, third, along };
            return true;
        }

        /// <summary>
        /// Least-squares direction of the spine across the band. Fitting the whole stretch rather
        /// than joining its two end stations keeps one noisy section from tilting the axis.
        /// </summary>
        private static bool TryFitSpineDirection(Spine[] spine, Band band, out double[] direction)
        {
            direction = null;

            var count = band.Count;
            double sumS = 0, sumSS = 0, sumU = 0, sumV = 0, sumSU = 0, sumSV = 0;
            for (var i = band.Start; i <= band.End; i++)
            {
                var s = spine[i].S;
                sumS += s;
                sumSS += s * s;
                sumU += spine[i].U;
                sumV += spine[i].V;
                sumSU += s * spine[i].U;
                sumSV += s * spine[i].V;
            }

            var denominator = count * sumSS - sumS * sumS;
            if (Math.Abs(denominator) < 1e-9)
            {
                return false;
            }

            var slopeU = (count * sumSU - sumS * sumU) / denominator;
            var slopeV = (count * sumSV - sumS * sumV) / denominator;

            direction = new[] { slopeU, slopeV, 1.0 };
            return TryNormalize(direction);
        }

        /// <summary>Furthest the spine strays from the straight line across the given stations.</summary>
        private static double SpineStray(Spine[] spine, Band band)
        {
            if (band.IsEmpty || band.Count < 3)
            {
                return double.MaxValue;
            }

            var first = spine[band.Start];
            var last = spine[band.End];
            var run = last.S - first.S;
            if (Math.Abs(run) < 1e-9)
            {
                return double.MaxValue;
            }

            var worst = 0.0;
            for (var i = band.Start; i <= band.End; i++)
            {
                var t = (spine[i].S - first.S) / run;
                var du = spine[i].U - (first.U + t * (last.U - first.U));
                var dv = spine[i].V - (first.V + t * (last.V - first.V));
                var stray = Math.Sqrt(du * du + dv * dv);
                if (stray > worst)
                {
                    worst = stray;
                }
            }

            return worst;
        }

        private struct Spine
        {
            public Spine(double u, double v, double s, double area)
            {
                U = u;
                V = v;
                S = s;
                Area = area;
            }

            public double U { get; }
            public double V { get; }
            public double S { get; }
            public double Area { get; }
        }

        private static readonly double[] UnitZ = { 0.0, 0.0, 1.0 };

        /// <summary>
        /// Re-cuts every station square to the part by aiming each plane along the local direction
        /// of the spine traced by the first pass's section centroids. On a body that curves along
        /// its length this is what stops the cut from running diagonally through the stock.
        /// </summary>
        private static List<(double Area, double Perimeter)> SliceAlongSpine(double[] local, Spine[] spine)
        {
            var sections = new List<(double Area, double Perimeter)>(spine.Length);

            for (var i = EdgeStationsSkipped; i < spine.Length - EdgeStationsSkipped; i++)
            {
                if (spine[i].Area <= 0)
                {
                    continue;
                }

                var before = spine[i - 1];
                var after = spine[i + 1];
                var normal = new[] { after.U - before.U, after.V - before.V, after.S - before.S };
                if (!TryNormalize(normal))
                {
                    normal = new[] { 0.0, 0.0, 1.0 };
                }

                var origin = new[] { spine[i].U, spine[i].V, spine[i].S };
                var cut = Slice(local, origin, normal, false);
                if (cut.Area > 0)
                {
                    sections.Add((cut.Area, cut.Perimeter));
                }
            }

            return sections;
        }

        /// <summary>
        /// Groups the stations into bands of equal section and returns the largest band. Notches
        /// and rounded ends fall into their own smaller bands, so the dominant band is the stock;
        /// it is reported at its median rather than its extreme, because two mirrored copies of
        /// one part tessellate slightly differently and would otherwise land on different numbers
        /// and split into two BOM rows.
        /// </summary>
        private static Result PickDominantSection(
            List<(double Area, double Perimeter)> sections,
            double lengthMm,
            double[] lengthAxis,
            bool straight,
            string label,
            double volumeMm3)
        {
            if (sections == null || sections.Count == 0)
            {
                return null;
            }

            sections.Sort((a, b) => a.Area.CompareTo(b.Area));

            var bestStart = 0;
            var bestCount = 0;
            var end = 0;
            for (var start = 0; start < sections.Count; start++)
            {
                if (end < start)
                {
                    end = start;
                }

                var ceiling = sections[start].Area * (1.0 + ClusterTolerance);
                while (end + 1 < sections.Count && sections[end + 1].Area <= ceiling)
                {
                    end++;
                }

                var count = end - start + 1;
                if (count > bestCount)
                {
                    bestCount = count;
                    bestStart = start;
                }
            }

            DiagnosticLog.Info(
                "BodyCrossSectionReader " + label + ": areas " + Fmt(sections[0].Area)
                + ".." + Fmt(sections[sections.Count - 1].Area)
                + " group " + bestCount + "/" + sections.Count
                + " at " + Fmt(sections[bestStart].Area) + ".." + Fmt(sections[bestStart + bestCount - 1].Area)
                + " length " + Fmt(lengthMm) + (straight ? " straight" : " curved")
                + " volume " + Fmt(volumeMm3));

            if (bestCount < sections.Count * MinClusterShare)
            {
                DiagnosticLog.Info(
                    "BodyCrossSectionReader " + label + ": no repeating section, rejected");
                return null;
            }

            var chosen = sections[bestStart + bestCount / 2];
            return new Result(
                chosen.Area, chosen.Perimeter, lengthMm, lengthAxis, straight, bestCount, sections.Count);
        }

        private struct Cut
        {
            public double Area;
            public double Perimeter;
            public double CentroidU;
            public double CentroidV;
        }

        /// <summary>
        /// Cuts the mesh with the plane through <paramref name="origin"/> normal to
        /// <paramref name="normal"/>. Each intersected triangle yields one oriented segment; by
        /// Green's theorem the segments sum to the enclosed area and centroid without having to be
        /// assembled into loops first, which keeps the cut robust where a section falls into
        /// several separate outlines.
        /// </summary>
        private static Cut Slice(double[] mesh, double[] origin, double[] normal, bool wantCentroid)
        {
            BuildPlaneBasis(normal, out var f, out var g);

            var cut = new Cut();
            var moment = 0.0;
            var momentU = 0.0;
            var momentV = 0.0;

            for (var t = 0; t + 8 < mesh.Length; t += 9)
            {
                var d0 = Dot(mesh, t, normal) - Dot(origin, normal);
                var d1 = Dot(mesh, t + 3, normal) - Dot(origin, normal);
                var d2 = Dot(mesh, t + 6, normal) - Dot(origin, normal);

                if ((d0 > 0 && d1 > 0 && d2 > 0) || (d0 <= 0 && d1 <= 0 && d2 <= 0))
                {
                    continue;
                }

                if (!TryCrossEdges(mesh, t, d0, d1, d2, out var pa, out var pb))
                {
                    continue;
                }

                if (!IsForward(mesh, t, normal, pa, pb))
                {
                    var swap = pa;
                    pa = pb;
                    pb = swap;
                }

                var au = Project(pa, origin, f);
                var av = Project(pa, origin, g);
                var bu = Project(pb, origin, f);
                var bv = Project(pb, origin, g);

                var cross = au * bv - bu * av;
                moment += cross;
                if (wantCentroid)
                {
                    momentU += (au + bu) * cross;
                    momentV += (av + bv) * cross;
                }

                var du = bu - au;
                var dv = bv - av;
                cut.Perimeter += Math.Sqrt(du * du + dv * dv);
            }

            cut.Area = Math.Abs(moment) / 2.0;

            if (wantCentroid && Math.Abs(moment) > 1e-9)
            {
                var localU = momentU / (3.0 * moment);
                var localV = momentV / (3.0 * moment);
                cut.CentroidU = origin[0] + localU * f[0] + localV * g[0];
                cut.CentroidV = origin[1] + localU * f[1] + localV * g[1];
            }
            else
            {
                cut.CentroidU = origin[0];
                cut.CentroidV = origin[1];
            }

            return cut;
        }

        private static bool TryCrossEdges(
            double[] mesh, int t, double d0, double d1, double d2, out double[] pa, out double[] pb)
        {
            pa = null;
            pb = null;

            for (var edge = 0; edge < 3; edge++)
            {
                var from = t + edge * 3;
                var to = t + ((edge + 1) % 3) * 3;
                var da = edge == 0 ? d0 : edge == 1 ? d1 : d2;
                var db = edge == 0 ? d1 : edge == 1 ? d2 : d0;

                if ((da > 0) == (db > 0))
                {
                    continue;
                }

                var ratio = da / (da - db);
                var point = new[]
                {
                    mesh[from] + ratio * (mesh[to] - mesh[from]),
                    mesh[from + 1] + ratio * (mesh[to + 1] - mesh[from + 1]),
                    mesh[from + 2] + ratio * (mesh[to + 2] - mesh[from + 2])
                };

                if (pa == null)
                {
                    pa = point;
                }
                else if (pb == null)
                {
                    pb = point;
                }
                else
                {
                    return false;
                }
            }

            return pa != null && pb != null;
        }

        /// <summary>
        /// The outline must run so the solid stays on its left, which for an outward-facing facet
        /// means along <c>normal × facet normal</c>.
        /// </summary>
        private static bool IsForward(double[] mesh, int t, double[] normal, double[] pa, double[] pb)
        {
            var e1 = new[] { mesh[t + 3] - mesh[t], mesh[t + 4] - mesh[t + 1], mesh[t + 5] - mesh[t + 2] };
            var e2 = new[] { mesh[t + 6] - mesh[t], mesh[t + 7] - mesh[t + 1], mesh[t + 8] - mesh[t + 2] };
            var facet = Cross(e1, e2);
            var want = Cross(normal, facet);
            var run = new[] { pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2] };
            return Dot(run, want) >= 0;
        }

        /// <summary>
        /// Length direction of the part itself: the principal axis of the mesh with the largest
        /// extent. Using the extent rather than the spread keeps a wide curved panel from being
        /// measured across its width when the two directions are close in size.
        /// </summary>
        private static double[][] TryBuildPartFrame(double[] mesh)
        {
            if (!TryCovariance(mesh, out var covariance))
            {
                return null;
            }

            var axes = Eigenvectors(covariance);

            var longest = 0;
            var longestExtent = -1.0;
            for (var i = 0; i < 3; i++)
            {
                ExtentAlong(mesh, axes[i], out var lo, out var hi);
                var extent = hi - lo;
                if (extent > longestExtent)
                {
                    longestExtent = extent;
                    longest = i;
                }
            }

            var along = axes[longest];
            var across = axes[(longest + 1) % 3];
            var third = Cross(along, across);
            if (!TryNormalize(third))
            {
                return null;
            }

            // Ordered so that across × third = along, keeping the cut plane right-handed.
            across = Cross(third, along);
            if (!TryNormalize(across))
            {
                return null;
            }

            return new[] { across, third, along };
        }

        private static bool TryCovariance(double[] mesh, out double[,] covariance)
        {
            covariance = new double[3, 3];

            var totalArea = 0.0;
            var mean = new double[3];
            var count = mesh.Length / 9;
            var centroids = new double[count * 3];
            var areas = new double[count];

            for (var i = 0; i < count; i++)
            {
                var t = i * 9;
                var e1 = new[] { mesh[t + 3] - mesh[t], mesh[t + 4] - mesh[t + 1], mesh[t + 5] - mesh[t + 2] };
                var e2 = new[] { mesh[t + 6] - mesh[t], mesh[t + 7] - mesh[t + 1], mesh[t + 8] - mesh[t + 2] };
                var normal = Cross(e1, e2);
                var area = 0.5 * Math.Sqrt(Dot(normal, normal));
                if (area <= 0)
                {
                    continue;
                }

                areas[i] = area;
                for (var c = 0; c < 3; c++)
                {
                    var centroid = (mesh[t + c] + mesh[t + 3 + c] + mesh[t + 6 + c]) / 3.0;
                    centroids[i * 3 + c] = centroid;
                    mean[c] += centroid * area;
                }

                totalArea += area;
            }

            if (totalArea <= 0)
            {
                return false;
            }

            for (var c = 0; c < 3; c++)
            {
                mean[c] /= totalArea;
            }

            for (var i = 0; i < count; i++)
            {
                if (areas[i] <= 0)
                {
                    continue;
                }

                var dx = centroids[i * 3] - mean[0];
                var dy = centroids[i * 3 + 1] - mean[1];
                var dz = centroids[i * 3 + 2] - mean[2];

                covariance[0, 0] += areas[i] * dx * dx;
                covariance[1, 1] += areas[i] * dy * dy;
                covariance[2, 2] += areas[i] * dz * dz;
                covariance[0, 1] += areas[i] * dx * dy;
                covariance[0, 2] += areas[i] * dx * dz;
                covariance[1, 2] += areas[i] * dy * dz;
            }

            covariance[1, 0] = covariance[0, 1];
            covariance[2, 0] = covariance[0, 2];
            covariance[2, 1] = covariance[1, 2];
            return true;
        }

        /// <summary>Cyclic Jacobi rotations — the matrix is a symmetric 3x3, so this converges fast.</summary>
        private static double[][] Eigenvectors(double[,] matrix)
        {
            var a = (double[,])matrix.Clone();
            var vectors = new double[3, 3];
            for (var i = 0; i < 3; i++)
            {
                vectors[i, i] = 1.0;
            }

            for (var sweep = 0; sweep < 32; sweep++)
            {
                var off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-12)
                {
                    break;
                }

                for (var p = 0; p < 2; p++)
                {
                    for (var q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-15)
                        {
                            continue;
                        }

                        var theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                        var sign = theta >= 0 ? 1.0 : -1.0;
                        var tangent = sign / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        var cos = 1.0 / Math.Sqrt(tangent * tangent + 1.0);
                        var sin = tangent * cos;

                        for (var k = 0; k < 3; k++)
                        {
                            var akp = a[k, p];
                            var akq = a[k, q];
                            a[k, p] = cos * akp - sin * akq;
                            a[k, q] = sin * akp + cos * akq;
                        }

                        for (var k = 0; k < 3; k++)
                        {
                            var apk = a[p, k];
                            var aqk = a[q, k];
                            a[p, k] = cos * apk - sin * aqk;
                            a[q, k] = sin * apk + cos * aqk;
                        }

                        for (var k = 0; k < 3; k++)
                        {
                            var vkp = vectors[k, p];
                            var vkq = vectors[k, q];
                            vectors[k, p] = cos * vkp - sin * vkq;
                            vectors[k, q] = sin * vkp + cos * vkq;
                        }
                    }
                }
            }

            var result = new double[3][];
            for (var i = 0; i < 3; i++)
            {
                var axis = new[] { vectors[0, i], vectors[1, i], vectors[2, i] };
                TryNormalize(axis);
                result[i] = axis;
            }

            return result;
        }

        private static double[] ToFrame(double[] mesh, double[][] frame)
        {
            var local = new double[mesh.Length];
            for (var i = 0; i + 2 < mesh.Length; i += 3)
            {
                for (var c = 0; c < 3; c++)
                {
                    local[i + c] = mesh[i] * frame[c][0] + mesh[i + 1] * frame[c][1] + mesh[i + 2] * frame[c][2];
                }
            }

            return local;
        }

        private static void Extent(double[] points, int component, out double low, out double high)
        {
            low = double.MaxValue;
            high = double.MinValue;
            for (var i = component; i < points.Length; i += 3)
            {
                if (points[i] < low)
                {
                    low = points[i];
                }

                if (points[i] > high)
                {
                    high = points[i];
                }
            }
        }

        private static void ExtentAlong(double[] points, double[] axis, out double low, out double high)
        {
            low = double.MaxValue;
            high = double.MinValue;
            for (var i = 0; i + 2 < points.Length; i += 3)
            {
                var projection = points[i] * axis[0] + points[i + 1] * axis[1] + points[i + 2] * axis[2];
                if (projection < low)
                {
                    low = projection;
                }

                if (projection > high)
                {
                    high = projection;
                }
            }
        }

        private static void BuildPlaneBasis(double[] normal, out double[] f, out double[] g)
        {
            var smallest = 0;
            for (var i = 1; i < 3; i++)
            {
                if (Math.Abs(normal[i]) < Math.Abs(normal[smallest]))
                {
                    smallest = i;
                }
            }

            var seed = new double[3];
            seed[smallest] = 1.0;

            f = Cross(seed, normal);
            if (!TryNormalize(f))
            {
                f = new[] { 1.0, 0.0, 0.0 };
            }

            g = Cross(normal, f);
            TryNormalize(g);
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double Dot(double[] mesh, int at, double[] b)
        {
            return mesh[at] * b[0] + mesh[at + 1] * b[1] + mesh[at + 2] * b[2];
        }

        private static double Project(double[] point, double[] origin, double[] axis)
        {
            return (point[0] - origin[0]) * axis[0]
                 + (point[1] - origin[1]) * axis[1]
                 + (point[2] - origin[2]) * axis[2];
        }

        private static bool TryNormalize(double[] vector)
        {
            var length = Math.Sqrt(Dot(vector, vector));
            if (length < 1e-12)
            {
                return false;
            }

            for (var i = 0; i < 3; i++)
            {
                vector[i] /= length;
            }

            return true;
        }

        /// <summary>
        /// Flat array of triangle vertices in millimetres, nine doubles per facet as
        /// <c>(x0,y0,z0, x1,y1,z1, x2,y2,z2)</c>.
        /// </summary>
        private static double[] ExtractTrianglesMm(Body2 body)
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
            tess.ImprovedQuality = true; // a coarse mesh clips the arc of a curved section short
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
                if (!vertexCache.TryGetValue(index, out var point))
                {
                    point = (double[])tess.GetVertexPoint(index);
                    vertexCache[index] = point;
                }

                return point;
            }

            var mesh = new double[facetCount * 9];

            for (var f = 0; f < facetCount; f++)
            {
                if (!(tess.GetFacetFins(f) is int[] fins) || fins.Length < 3)
                {
                    continue;
                }

                var p0 = GetVertex(((int[])tess.GetFinVertices(fins[0]))[0]);
                var p1 = GetVertex(((int[])tess.GetFinVertices(fins[1]))[0]);
                var p2 = GetVertex(((int[])tess.GetFinVertices(fins[2]))[0]);

                var at = f * 9;
                for (var c = 0; c < 3; c++)
                {
                    mesh[at + c] = p0[c] * 1000.0;
                    mesh[at + 3 + c] = p1[c] * 1000.0;
                    mesh[at + 6 + c] = p2[c] * 1000.0;
                }
            }

            return mesh;
        }

        private static string Fmt(double value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
