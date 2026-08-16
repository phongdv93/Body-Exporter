using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Stock width from the body's straight edges — the same measurement a user takes with
    /// Smart Dimension on the two side edges of a curved panel.
    ///
    /// <para>
    /// Cross-section area ÷ thickness under-reads boards that curve hard across their width:
    /// the tessellated cut loses outline, so a 160 mm panel came back as 131 mm. The B-rep
    /// edges are exact. A rectangular board has at least two straight edges equal to its
    /// width (the sides of the end faces, or the short sides of the skins); those edges
    /// survive bending when the bend runs along the length.
    /// </para>
    /// </summary>
    internal static class BodyStockEdgeReader
    {
        /// <summary>Edges shorter than this above the thickness are just the thickness itself.</summary>
        private const double MinAboveThicknessMm = 1.0;

        /// <summary>Two edges within this band are the same stock size.</summary>
        private const double MatchToleranceMm = 0.5;

        /// <summary>
        /// How far an edge may lean off square to the length axis, as |cos| of the angle between
        /// them. A stock width edge crosses the part; a mitre or chamfer edge runs diagonally and
        /// must be rejected, or a corner gusset reads its 62 mm diagonal as its 55 mm width.
        /// The allowance covers curved parts, whose end edges sit square to the local tangent
        /// rather than to the chord the length is measured along.
        /// </summary>
        private const double MaxLengthAxisLean = 0.3;

        public static double? TryReadWidthMillimeters(
            Body2 body,
            double thicknessMm,
            double lengthMm,
            double[] lengthAxis,
            string label)
        {
            if (body == null || thicknessMm <= 0)
            {
                return null;
            }

            var lengths = TryReadStraightEdgeLengths(body, lengthAxis);
            if (lengths == null || lengths.Count == 0)
            {
                return null;
            }

            // Width sits between thickness and length. Edges that are essentially the part's
            // length (within 2%) must not be mistaken for the stock width — but a near-square
            // panel (seat 475×453) still has to keep its width edges.
            var upper = lengthMm > thicknessMm * 2
                ? lengthMm * 0.98
                : double.MaxValue;

            var candidates = new List<double>();
            foreach (var length in lengths)
            {
                if (length <= thicknessMm + MinAboveThicknessMm)
                {
                    continue;
                }

                if (length >= upper)
                {
                    continue;
                }

                candidates.Add(length);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            candidates.Sort();

            // Largest length that repeats (pair of matching side edges). A one-off long edge
            // on a chamfer or mitre is ignored; the stock width always appears at least twice.
            double? best = null;
            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var value = candidates[i];
                var matches = 0;
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (Math.Abs(candidates[j] - value) <= MatchToleranceMm)
                    {
                        matches++;
                    }
                }

                if (matches >= 2)
                {
                    best = value;
                    break;
                }
            }

            if (!best.HasValue)
            {
                DiagnosticLog.Info(
                    "BodyStockEdgeReader " + label + ": no repeated width edge between "
                    + Fmt(thicknessMm) + " and " + Fmt(upper));
                return null;
            }

            DiagnosticLog.Info(
                "BodyStockEdgeReader " + label + ": width edge " + Fmt(best.Value)
                + " (thickness " + Fmt(thicknessMm) + ", length cap " + Fmt(upper) + ")");

            return best;
        }

        private static List<double> TryReadStraightEdgeLengths(Body2 body, double[] lengthAxis)
        {
            object[] edgesObj;
            try
            {
                edgesObj = body.GetEdges() as object[];
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyStockEdgeReader.edges: " + ex.Message);
                return null;
            }

            if (edgesObj == null || edgesObj.Length == 0)
            {
                return null;
            }

            var lengths = new List<double>(edgesObj.Length);
            foreach (var edgeObj in edgesObj)
            {
                if (!(edgeObj is Edge edge))
                {
                    continue;
                }

                Curve curve;
                try
                {
                    curve = edge.GetCurve() as Curve;
                }
                catch
                {
                    continue;
                }

                if (curve == null)
                {
                    continue;
                }

                try
                {
                    if (!curve.IsLine())
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                var start = (edge.GetStartVertex() as Vertex)?.GetPoint() as double[];
                var end = (edge.GetEndVertex() as Vertex)?.GetPoint() as double[];
                if (start == null || end == null || start.Length < 3 || end.Length < 3)
                {
                    continue;
                }

                var dx = start[0] - end[0];
                var dy = start[1] - end[1];
                var dz = start[2] - end[2];
                var span = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (span <= 0)
                {
                    continue;
                }

                if (!CrossesLengthAxis(dx / span, dy / span, dz / span, lengthAxis))
                {
                    continue;
                }

                lengths.Add(span * 1000.0);
            }

            return lengths;
        }

        private static bool CrossesLengthAxis(double ux, double uy, double uz, double[] axis)
        {
            if (axis == null || axis.Length < 3)
            {
                return true;
            }

            var dot = Math.Abs(ux * axis[0] + uy * axis[1] + uz * axis[2]);
            return dot <= MaxLengthAxisLean;
        }

        private static string Fmt(double value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
