using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Reads sheet / arc / curved-panel wall thickness from cylindrical or parallel planar faces.
    /// E.g. C-hook: 50 mm wall, not 250 mm radius span. Curved seat panel: 28 mm wall, not arc chord.
    /// </summary>
    internal static class BodyProfileThicknessReader
    {
        private const double AxisParallelTolerance = 0.02;
        private const double MinWallMillimeters = 3;
        private const double MaxWallMillimeters = 120;
        /// <summary>Ignore hole / fillet cylinders below this radius when inferring sheet wall.</summary>
        private const double MinProfileCylinderRadiusMillimeters = 30;
        /// <summary>Minimum planar face area to consider when looking for parallel sheet faces.</summary>
        private const double MinPlanarAreaSquareMillimeters = 5000;
        /// <summary>Max ratio between the two largest planar parallel faces (smallest is the wall).</summary>
        private const double MaxPlanarFaceAreaRatio = 3.0;
        /// <summary>Max ratio between the two dominant skin faces before volume/area is untrustworthy.</summary>
        private const double MaxSkinAreaRatio = 2.0;
        /// <summary>How far an exact candidate may sit from the volume/area reference.</summary>
        private const double MaxReferenceDeviation = 0.15;

        public static double? TryReadWallThicknessMillimeters(Body2 body)
        {
            return TryReadWallThicknessMillimeters(body, 0);
        }

        /// <param name="bboxVolumeMm3">Bounding-box volume, used to validate the mass-property read.</param>
        public static double? TryReadWallThicknessMillimeters(Body2 body, double bboxVolumeMm3)
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

                // Volume / skin area is unbiased but reads slightly high on panels whose edges
                // are rounded, so it acts as the reference that picks between the exact
                // geometric candidates rather than as the answer itself.
                var reference = InRange(TryReadFromVolumeAndSkinAreas(body, facesObj, bboxVolumeMm3));

                var candidates = new List<KeyValuePair<string, double>>();
                AddCandidate(candidates, "cylinders", TryReadFromCylinders(facesObj));
                AddCandidate(candidates, "face-gap", TryReadFromLargestFaceGap(facesObj));
                AddCandidate(candidates, "planar", TryReadFromParallelPlanarFaces(facesObj));
                AddCandidate(candidates, "edges", TryReadFromStraightEdgeCluster(body));

                if (reference.HasValue && candidates.Count > 0)
                {
                    var best = candidates
                        .OrderBy(c => Math.Abs(c.Value - reference.Value))
                        .First();
                    if (Math.Abs(best.Value - reference.Value) <= reference.Value * MaxReferenceDeviation)
                    {
                        LogWall(best.Key, best.Value, reference);
                        return RoundWall(best.Value);
                    }

                    LogWall("skins", reference.Value, reference);
                    return RoundWall(reference.Value);
                }

                if (reference.HasValue)
                {
                    LogWall("skins", reference.Value, null);
                    return RoundWall(reference.Value);
                }

                if (candidates.Count > 0)
                {
                    LogWall(candidates[0].Key, candidates[0].Value, null);
                    return RoundWall(candidates[0].Value);
                }

                return null;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyProfileThicknessReader: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Cylindrical wall detection: two concentric cylinders with similar axis imply the body is a
        /// sheet wrapped around an arc. Wall = |R1 - R2|.
        /// </summary>
        private static double? TryReadFromCylinders(object[] facesObj)
        {
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
                    if (gap >= MinWallMillimeters && gap <= MaxWallMillimeters && gap < bestGap
                        && (cylinders[i].RadiusMm >= MinProfileCylinderRadiusMillimeters
                            || cylinders[j].RadiusMm >= MinProfileCylinderRadiusMillimeters))
                    {
                        bestGap = gap;
                    }
                }
            }

            return bestGap < double.MaxValue ? bestGap : (double?)null;
        }

        /// <summary>
        /// Planar parallel-face wall detection. A curved panel of constant thickness has two large,
        /// opposite-facing planar surfaces (top + bottom) that are parallel and separated by the
        /// wall thickness. Catches curved seats, curved planks, bent sheet metal etc. that have no
        /// cylindrical face to read from.
        /// </summary>
        private static double? TryReadFromParallelPlanarFaces(object[] facesObj)
        {
            var planes = new List<PlaneSample>();
            foreach (var faceObj in facesObj)
            {
                if (!(faceObj is Face2 face))
                {
                    continue;
                }

                var surf = face.GetSurface() as Surface;
                if (surf == null || !surf.IsPlane())
                {
                    continue;
                }

                var p = surf.PlaneParams as double[];
                if (p == null || p.Length < 6)
                {
                    continue;
                }

                // PlaneParams = [a, b, c, d] where a*x + b*y + c*z = d
                // [a,b,c] is the normal vector (may not be unit length).
                // d is the distance from origin along the normal direction.
                var normal = NormalizeAxis(p[0], p[1], p[2]);
                if (normal == null)
                {
                    continue;
                }

                double[] box;
                try
                {
                    box = (double[])face.GetBox();
                }
                catch
                {
                    box = null;
                }

                if (box == null || box.Length < 6)
                {
                    planes.Add(new PlaneSample(normal, MinPlanarAreaSquareMillimeters, null));
                    continue;
                }

                // Use face bounding box to compute face center and area.
                var centerX = (box[0] + box[3]) * 0.5;
                var centerY = (box[1] + box[4]) * 0.5;
                var centerZ = (box[2] + box[5]) * 0.5;
                var centerMm = new[] { centerX * 1000.0, centerY * 1000.0, centerZ * 1000.0 };

                // Compute face area: the smallest bbox dimension is the face "thickness" perpendicular
                // to the planar face; the area is the product of the other two dimensions.
                var sortedSizes = new[] { (box[3] - box[0]) * 1000.0, (box[4] - box[1]) * 1000.0, (box[5] - box[2]) * 1000.0 }
                    .OrderBy(v => v)
                    .ToArray();
                var areaMm2 = sortedSizes[1] * sortedSizes[2];
                if (areaMm2 < MinPlanarAreaSquareMillimeters)
                {
                    continue;
                }

                planes.Add(new PlaneSample(normal, areaMm2, centerMm));
            }

            if (planes.Count < 2)
            {
                return null;
            }

            var bestGap = double.MaxValue;
            var bestPairArea = 0.0;
            for (var i = 0; i < planes.Count; i++)
            {
                for (var j = i + 1; j < planes.Count; j++)
                {
                    if (planes[i].CenterMm == null || planes[j].CenterMm == null)
                    {
                        continue;
                    }

                    if (!PlanesAreParallel(planes[i].Normal, planes[j].Normal))
                    {
                        continue;
                    }

                    // Compute gap between two parallel planes by projecting face centers onto the
                    // shared normal direction and taking the perpendicular distance.
                    // gap = |dot(c2 - c1, normal)| where c1, c2 are face centers in mm.
                    var dx = planes[j].CenterMm[0] - planes[i].CenterMm[0];
                    var dy = planes[j].CenterMm[1] - planes[i].CenterMm[1];
                    var dz = planes[j].CenterMm[2] - planes[i].CenterMm[2];
                    var gap = Math.Abs(dx * planes[i].Normal[0]
                                     + dy * planes[i].Normal[1]
                                     + dz * planes[i].Normal[2]);

                    if (gap < MinWallMillimeters || gap > MaxWallMillimeters)
                    {
                        continue;
                    }

                    var areaRatio = planes[i].AreaMm2 >= planes[j].AreaMm2
                        ? planes[i].AreaMm2 / Math.Max(planes[j].AreaMm2, 1e-6)
                        : planes[j].AreaMm2 / Math.Max(planes[i].AreaMm2, 1e-6);
                    if (areaRatio > MaxPlanarFaceAreaRatio)
                    {
                        continue;
                    }

                    // Prefer the pair with the largest combined area. This rejects vát-mép
                    // pairings (small width) in favour of the genuine top/bottom sheet pair
                    // (largest planar faces on the body).
                    var pairArea = planes[i].AreaMm2 + planes[j].AreaMm2;
                    DiagnosticLog.Info(
                        "BodyProfileThicknessReader.PlanarPair: gap=" + gap.ToString("F3")
                        + "mm areas=[" + planes[i].AreaMm2.ToString("F0") + ", "
                        + planes[j].AreaMm2.ToString("F0") + "] normals=["
                        + planes[i].Normal[0].ToString("F2") + ","
                        + planes[i].Normal[1].ToString("F2") + ","
                        + planes[i].Normal[2].ToString("F2") + "]");
                    if (pairArea > bestPairArea * 1.05 || (gap < bestGap && pairArea >= bestPairArea * 0.95))
                    {
                        bestGap = gap;
                        bestPairArea = pairArea;
                    }
                }
            }

            if (bestGap < double.MaxValue)
            {
                DiagnosticLog.Info(
                    "BodyProfileThicknessReader.PlanarBest: gap=" + bestGap.ToString("F3")
                    + "mm pairArea=" + bestPairArea.ToString("F0"));
            }

            return bestGap < double.MaxValue ? bestGap : (double?)null;
        }

        /// <summary>
        /// Panel thickness from solid volume and the two dominant skin areas:
        /// V = skin × t, so t = 2V / (A1 + A2). Requires the two largest faces to be
        /// comparable in area, which rules out rods and blobs.
        /// </summary>
        private static double? TryReadFromVolumeAndSkinAreas(Body2 body, object[] facesObj, double bboxVolumeMm3)
        {
            var areas = new List<double>();
            foreach (var faceObj in facesObj)
            {
                if (!(faceObj is Face2 face))
                {
                    continue;
                }

                try
                {
                    var areaMm2 = face.GetArea() * 1_000_000.0;
                    if (areaMm2 > 0)
                    {
                        areas.Add(areaMm2);
                    }
                }
                catch
                {
                    // Face area is unavailable on some imported geometry; skip it.
                }
            }

            if (areas.Count < 2)
            {
                return null;
            }

            areas.Sort();
            areas.Reverse();
            var a1 = areas[0];
            var a2 = areas[1];
            if (a2 <= 0 || a1 / a2 > MaxSkinAreaRatio)
            {
                return null;
            }

            var volume = BodyVolumeReader.TryReadCubicMillimeters(body, bboxVolumeMm3);
            if (!volume.HasValue)
            {
                return null;
            }

            return 2.0 * volume.Value / (a1 + a2);
        }

        /// <summary>
        /// Distance between the two largest faces (the skins of a panel). Works for cylinder,
        /// spline, and mixed curvature as long as the faces are true offsets.
        /// </summary>
        private static double? TryReadFromLargestFaceGap(object[] facesObj)
        {
            var samples = new List<FaceAreaSample>();
            foreach (var faceObj in facesObj)
            {
                if (!(faceObj is Face2 face))
                {
                    continue;
                }

                double areaMm2;
                try
                {
                    areaMm2 = face.GetArea() * 1_000_000.0;
                }
                catch
                {
                    continue;
                }

                if (areaMm2 < MinPlanarAreaSquareMillimeters)
                {
                    continue;
                }

                double[] box;
                try
                {
                    box = (double[])face.GetBox();
                }
                catch
                {
                    box = null;
                }

                if (box == null || box.Length < 6)
                {
                    continue;
                }

                samples.Add(new FaceAreaSample(
                    face,
                    areaMm2,
                    (box[0] + box[3]) * 0.5,
                    (box[1] + box[4]) * 0.5,
                    (box[2] + box[5]) * 0.5));
            }

            if (samples.Count < 2)
            {
                return null;
            }

            var ranked = samples.OrderByDescending(s => s.AreaMm2).Take(8).ToList();
            var bestGap = double.MaxValue;
            var bestArea = 0.0;
            for (var i = 0; i < ranked.Count; i++)
            {
                for (var j = i + 1; j < ranked.Count; j++)
                {
                    var gap = FacePairGapMillimeters(ranked[i], ranked[j]);
                    if (!gap.HasValue || gap.Value < MinWallMillimeters || gap.Value > MaxWallMillimeters)
                    {
                        continue;
                    }

                    var pairArea = ranked[i].AreaMm2 + ranked[j].AreaMm2;
                    if (pairArea > bestArea * 1.05 || (gap.Value < bestGap && pairArea >= bestArea * 0.95))
                    {
                        bestGap = gap.Value;
                        bestArea = pairArea;
                    }
                }
            }

            return bestGap < double.MaxValue ? bestGap : (double?)null;
        }

        private static double? FacePairGapMillimeters(FaceAreaSample a, FaceAreaSample b)
        {
            // The box centre of a curved face floats in space between the skins, so measuring
            // straight from it lands on a rounded edge and under-reports the wall. Snap onto
            // face A first, then step across to face B.
            var onA = ClosestPointOnFace(a.Face, a.Cx, a.Cy, a.Cz);
            if (onA == null)
            {
                return null;
            }

            var onB = ClosestPointOnFace(b.Face, onA[0], onA[1], onA[2]);
            if (onB == null)
            {
                return null;
            }

            var dx = onA[0] - onB[0];
            var dy = onA[1] - onB[1];
            var dz = onA[2] - onB[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;
        }

        private static double[] ClosestPointOnFace(Face2 face, double x, double y, double z)
        {
            try
            {
                if (!(face.GetClosestPointOn(x, y, z) is double[] p) || p.Length < 3)
                {
                    return null;
                }

                return new[] { p[0], p[1], p[2] };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extruded / swept panels have several straight edges equal to the stock thickness
        /// (the radial cuts). Ignore one-off short chamfers; need a repeated length.
        /// </summary>
        private static double? TryReadFromStraightEdgeCluster(Body2 body)
        {
            object[] edgesObj;
            try
            {
                edgesObj = body.GetEdges() as object[];
            }
            catch
            {
                return null;
            }

            if (edgesObj == null || edgesObj.Length == 0)
            {
                return null;
            }

            var counts = new Dictionary<int, int>();
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
                var lenMm = Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;
                if (lenMm < MinWallMillimeters || lenMm > MaxWallMillimeters)
                {
                    continue;
                }

                var key = (int)Math.Round(lenMm * 10.0, MidpointRounding.AwayFromZero);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            var best = counts
                .Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .FirstOrDefault();
            if (best.Value < 2)
            {
                return null;
            }

            return best.Key / 10.0;
        }

        private static double? InRange(double? wallMm)
        {
            if (!wallMm.HasValue || wallMm.Value < MinWallMillimeters || wallMm.Value > MaxWallMillimeters)
            {
                return null;
            }

            return wallMm.Value;
        }

        private static void AddCandidate(List<KeyValuePair<string, double>> target, string source, double? wallMm)
        {
            var value = InRange(wallMm);
            if (value.HasValue)
            {
                target.Add(new KeyValuePair<string, double>(source, value.Value));
            }
        }

        private static void LogWall(string source, double wallMm, double? reference)
        {
            DiagnosticLog.Info(
                "BodyProfileThicknessReader: " + source + "="
                + wallMm.ToString("F3", CultureInfo.InvariantCulture)
                + (reference.HasValue
                    ? " ref=" + reference.Value.ToString("F3", CultureInfo.InvariantCulture)
                    : string.Empty));
        }

        private static double RoundWall(double wallMm)
        {
            return Math.Round(wallMm, 3, MidpointRounding.AwayFromZero);
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

        private static bool PlanesAreParallel(double[] a, double[] b)
        {
            // Two planes are parallel when their normals are parallel OR antiparallel.
            return AxesParallel(a, b);
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

        private sealed class PlaneSample
        {
            public PlaneSample(double[] normal, double areaMm2, double[] centerMm)
            {
                Normal = normal;
                AreaMm2 = areaMm2;
                CenterMm = centerMm;
            }

            public double[] Normal { get; }
            public double AreaMm2 { get; }
            public double[] CenterMm { get; }
        }

        private sealed class FaceAreaSample
        {
            public FaceAreaSample(Face2 face, double areaMm2, double cx, double cy, double cz)
            {
                Face = face;
                AreaMm2 = areaMm2;
                Cx = cx;
                Cy = cy;
                Cz = cz;
            }

            public Face2 Face { get; }
            public double AreaMm2 { get; }
            public double Cx { get; }
            public double Cy { get; }
            public double Cz { get; }
        }
    }
}
