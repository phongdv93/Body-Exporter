using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Reads sheet / arc profile wall thickness from cylindrical faces (e.g. C-hook: 50 mm wall, not 250 mm radius span).
    /// </summary>
    internal static class BodyProfileThicknessReader
    {
        private const double AxisParallelTolerance = 0.02;
        private const double MinWallMillimeters = 3;
        private const double MaxWallMillimeters = 120;

        public static double? TryReadWallThicknessMillimeters(Body2 body)
        {
            if (body == null)
            {
                return null;
            }

            try
            {
                var facesObj = body.GetFaces() as object[];
                if (facesObj == null || facesObj.Length == 0)
                {
                    return null;
                }

                var cylinders = new List<CylinderSample>();
                foreach (var faceObj in facesObj)
                {
                    if (!(faceObj is Face2 face))
                    {
                        continue;
                    }

                    var surf = face.GetSurface() as Surface;
                    if (surf == null || !surf.IsCylinder())
                    {
                        continue;
                    }

                    var p = surf.CylinderParams as double[];
                    if (p == null || p.Length < 7)
                    {
                        continue;
                    }

                    var radiusMm = Math.Abs(p[6]) * 1000.0;
                    if (radiusMm < MinWallMillimeters)
                    {
                        continue;
                    }

                    var axis = NormalizeAxis(p[3], p[4], p[5]);
                    if (axis == null)
                    {
                        continue;
                    }

                    cylinders.Add(new CylinderSample(axis, radiusMm));
                }

                if (cylinders.Count < 2)
                {
                    return null;
                }

                var bestGap = double.MaxValue;
                for (var i = 0; i < cylinders.Count; i++)
                {
                    for (var j = i + 1; j < cylinders.Count; j++)
                    {
                        if (!AxesParallel(cylinders[i].Axis, cylinders[j].Axis))
                        {
                            continue;
                        }

                        var gap = Math.Abs(cylinders[i].RadiusMm - cylinders[j].RadiusMm);
                        if (gap >= MinWallMillimeters && gap <= MaxWallMillimeters && gap < bestGap)
                        {
                            bestGap = gap;
                        }
                    }
                }

                return bestGap < double.MaxValue
                    ? Math.Round(bestGap, 3, MidpointRounding.AwayFromZero)
                    : (double?)null;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyProfileThicknessReader: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// For curved profiles: keep extrusion thickness, replace radial "span" with wall when bbox middle looks like arc radius.
        /// </summary>
        public static (double X, double Y, double Z) AdjustBoundingSizeForCurvedProfile(
            double x,
            double y,
            double z,
            double wallMm)
        {
            if (wallMm <= 0)
            {
                return (x, y, z);
            }

            var ordered = new[] { x, y, z }.OrderBy(v => v).ToArray();
            var min = ordered[0];
            var mid = ordered[1];
            var max = ordered[2];

            if (max < wallMm * 2.5)
            {
                return (x, y, z);
            }

            // Smallest bbox axis ≈ extrusion direction — snap to measured wall.
            if (Math.Abs(min - wallMm) <= Math.Max(8, wallMm * 0.35))
            {
                min = wallMm;
            }

            // Middle axis often holds outer radius (~250) while true stock width is wall (~50).
            if (mid >= wallMm * 2 && mid <= wallMm * 12 && mid > wallMm * 1.5)
            {
                mid = wallMm;
            }

            var sized = new[] { min, mid, max };
            return ReassignOrderedToAxes(x, y, z, sized);
        }

        private static (double X, double Y, double Z) ReassignOrderedToAxes(
            double x,
            double y,
            double z,
            double[] orderedAsc)
        {
            var original = new[] { x, y, z };
            var result = new[] { x, y, z };
            var used = new bool[3];

            for (var o = 0; o < 3; o++)
            {
                var target = orderedAsc[o];
                var bestIdx = -1;
                var bestDiff = double.MaxValue;
                for (var i = 0; i < 3; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    var diff = Math.Abs(original[i] - target);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0)
                {
                    used[bestIdx] = true;
                    result[bestIdx] = target;
                }
            }

            return (result[0], result[1], result[2]);
        }

        private static double[] NormalizeAxis(double ax, double ay, double az)
        {
            var len = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (len < 1e-9)
            {
                return null;
            }

            return new[] { ax / len, ay / len, az / len };
        }

        private static bool AxesParallel(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            var dot = Math.Abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2]);
            return dot >= 1.0 - AxisParallelTolerance;
        }

        private sealed class CylinderSample
        {
            public CylinderSample(double[] axis, double radiusMm)
            {
                Axis = axis;
                RadiusMm = radiusMm;
            }

            public double[] Axis { get; }
            public double RadiusMm { get; }
        }
    }
}
