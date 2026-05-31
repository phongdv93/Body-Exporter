using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// For extruded wave / S-profile bodies, global bbox width (e.g. 102 mm) can exceed the
    /// stock width at any cross-section (e.g. 50 mm). Samples edge vertices along the length axis.
    /// </summary>
    internal static class BodyProfileWidthReader
    {
        private const int SliceCount = 28;
        private const double MinMiddleToThicknessRatio = 2.0;
        private const double MinLengthToThicknessRatio = 5.0;

        public static double? TryMeasureMaxCrossSectionWidthMillimeters(Body2 body, double xMm, double yMm, double zMm)
        {
            if (body == null)
            {
                return null;
            }

            var sizes = new[] { xMm, yMm, zMm }.OrderBy(v => v).ToArray();
            var thickness = sizes[0];
            var middle = sizes[1];
            var length = sizes[2];

            if (thickness <= 0 || middle <= thickness || length <= middle)
            {
                return null;
            }

            if (middle / thickness < MinMiddleToThicknessRatio || length / thickness < MinLengthToThicknessRatio)
            {
                return null;
            }

            var thicknessAxis = AxisIndexForSize(xMm, yMm, zMm, thickness);
            var lengthAxis = AxisIndexForSize(xMm, yMm, zMm, length);
            if (thicknessAxis < 0 || lengthAxis < 0 || thicknessAxis == lengthAxis)
            {
                return null;
            }

            var widthAxis = RemainingAxis(thicknessAxis, lengthAxis);

            var vertices = CollectVerticesMillimeters(body);
            if (vertices.Count < 12)
            {
                return null;
            }

            var lengthCoords = vertices.Select(v => v[lengthAxis]).ToList();
            var lMin = lengthCoords.Min();
            var lMax = lengthCoords.Max();
            var span = lMax - lMin;
            if (span < thickness * 2)
            {
                return null;
            }

            var sliceLen = span / SliceCount;
            var maxLocalWidth = 0.0;

            for (var i = 0; i < SliceCount; i++)
            {
                var sliceCenter = lMin + (i + 0.5) * sliceLen;
                var halfBand = sliceLen * 0.55;
                var inSlice = vertices
                    .Where(v => Math.Abs(v[lengthAxis] - sliceCenter) <= halfBand)
                    .ToList();
                if (inSlice.Count < 2)
                {
                    continue;
                }

                var wMin = inSlice.Min(v => v[widthAxis]);
                var wMax = inSlice.Max(v => v[widthAxis]);
                maxLocalWidth = Math.Max(maxLocalWidth, wMax - wMin);
            }

            maxLocalWidth = Math.Round(maxLocalWidth, 3, MidpointRounding.AwayFromZero);
            if (maxLocalWidth <= thickness * 0.75 || maxLocalWidth >= middle * 0.95)
            {
                return null;
            }

            return maxLocalWidth;
        }

        public static (double X, double Y, double Z) ReplaceAxisMillimeters(
            double x,
            double y,
            double z,
            int axisIndex,
            double newValueMm)
        {
            switch (axisIndex)
            {
                case 0:
                    return (newValueMm, y, z);
                case 1:
                    return (x, newValueMm, z);
                default:
                    return (x, y, newValueMm);
            }
        }

        public static int MiddleAxisIndex(double xMm, double yMm, double zMm)
        {
            var ranked = new[]
                {
                    (Index: 0, Value: xMm),
                    (Index: 1, Value: yMm),
                    (Index: 2, Value: zMm)
                }
                .OrderBy(item => item.Value)
                .ToArray();

            return ranked[1].Index;
        }

        private static int AxisIndexForSize(double xMm, double yMm, double zMm, double targetMm)
        {
            var values = new[] { xMm, yMm, zMm };
            var bestIdx = 0;
            var bestDiff = double.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var diff = Math.Abs(values[i] - targetMm);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIdx = i;
                }
            }

            return bestDiff <= 0.2 ? bestIdx : -1;
        }

        private static int RemainingAxis(int axisA, int axisB)
        {
            for (var i = 0; i < 3; i++)
            {
                if (i != axisA && i != axisB)
                {
                    return i;
                }
            }

            return 1;
        }

        private static List<double[]> CollectVerticesMillimeters(Body2 body)
        {
            var points = new List<double[]>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                var edgesObj = body.GetEdges() as object[];
                if (edgesObj == null)
                {
                    return points;
                }

                foreach (var edgeObj in edgesObj)
                {
                    if (!(edgeObj is Edge edge))
                    {
                        continue;
                    }

                    foreach (var vertexObj in new object[] { edge.GetStartVertex(), edge.GetEndVertex() })
                    {
                        if (!(vertexObj is Vertex vertex))
                        {
                            continue;
                        }

                        var raw = vertex.GetPoint() as double[];
                        if (raw == null || raw.Length < 3)
                        {
                            continue;
                        }

                        var mm = new[]
                        {
                            raw[0] * 1000.0,
                            raw[1] * 1000.0,
                            raw[2] * 1000.0
                        };
                        var key = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:F4}|{1:F4}|{2:F4}",
                            mm[0],
                            mm[1],
                            mm[2]);
                        if (seen.Add(key))
                        {
                            points.Add(mm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyProfileWidthReader: " + ex.Message);
            }

            return points;
        }
    }
}
