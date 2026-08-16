using System;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Stock size for the BOM. Starts from the axis-aligned bounding box, then corrects the two
    /// small axes for curved parts: thickness comes from the panel wall, and width from the real
    /// cross-section cut across the length. The box is wrong in both directions on curved parts —
    /// a bowed slat reads too wide because the box spans the arc rise, while a panel curved across
    /// its width reads too narrow because the box measures the chord instead of the flat board.
    /// </summary>
    internal static class BodyDimensionCalculator
    {
        /// <summary>Only override a bbox axis when the measured value is smaller by this much.</summary>
        private const double MinRelativeGain = 0.015;
        private const double MinAbsoluteGainMm = 1.0;

        /// <summary>
        /// Leave the bounding box alone unless the section disagrees with it by more than this,
        /// so straight parts keep reporting the exact box figure instead of drifting by a decimal.
        /// </summary>
        private const double MinWidthChangeMm = 1.0;
        private const double MinWidthChangeRatio = 0.015;

        /// <summary>A section width beyond this multiple of the box is not a plausible board.</summary>
        private const double MaxWidthToBoxRatio = 4.0;

        /// <summary>
        /// How far a side edge may sit above the section reading before it is believed instead of
        /// it. Below the lower bound the two agree and the section stands; above the upper bound
        /// the edge is measuring some other feature.
        /// </summary>
        private const double MinEdgeOverSectionRatio = 1.05;
        private const double MaxEdgeOverSectionRatio = 1.5;

        public static (double X, double Y, double Z,
                      DimensionAxis LengthAxis, DimensionAxis WidthAxis, DimensionAxis ThicknessAxis)
            ComputeDimensions(Body2 body)
        {
            if (body == null)
            {
                return (0, 0, 0, DimensionAxis.X, DimensionAxis.Y, DimensionAxis.Z);
            }

            var box = (double[])body.GetBodyBox();
            if (box == null || box.Length < 6)
            {
                return (0, 0, 0, DimensionAxis.X, DimensionAxis.Y, DimensionAxis.Z);
            }

            var x = Math.Abs(box[3] - box[0]) * 1000.0;
            var y = Math.Abs(box[4] - box[1]) * 1000.0;
            var z = Math.Abs(box[5] - box[2]) * 1000.0;
            var bboxVolume = x * y * z;

            var ranked = new[]
            {
                (Index: 0, Value: x),
                (Index: 1, Value: y),
                (Index: 2, Value: z)
            }.OrderByDescending(item => item.Value).ToArray();

            var runAxis = ranked[0].Index;
            var wideAxis = ranked[1].Index;
            var thinAxis = ranked[2].Index;
            var bboxRun = ranked[0].Value;
            var bboxWide = ranked[1].Value;
            var bboxThin = ranked[2].Value;

            var wall = BodyProfileThicknessReader.TryReadWallThicknessMillimeters(body, bboxVolume);
            var thickness = wall.HasValue && IsGain(wall.Value, bboxThin) ? wall.Value : (double?)null;

            var volume = BodyVolumeReader.TryReadCubicMillimeters(body, bboxVolume);
            var section = BodyCrossSectionReader.TryMeasure(body, SafeName(body), volume);
            var lengthHint = section != null && section.LengthMm > 0 ? section.LengthMm : bboxRun;
            var width = TryPickWidth(
                body, section, thickness ?? bboxThin, bboxWide, lengthHint);
            var length = TryPickLength(body, section, width ?? bboxWide, thickness ?? bboxThin, bboxRun, volume);

            if (thickness == null && width == null && length == null)
            {
                return Finish(body, x, y, z, ranked, wall, null, null);
            }

            var result = new[] { x, y, z };
            if (length.HasValue)
            {
                result[runAxis] = Round(length.Value);
            }

            if (width.HasValue)
            {
                result[wideAxis] = Round(width.Value);
            }

            if (thickness.HasValue)
            {
                result[thinAxis] = Round(thickness.Value);
            }

            // A corrected thickness can end up above a corrected width; keep width the larger one.
            if (result[thinAxis] > result[wideAxis])
            {
                var swap = result[thinAxis];
                result[thinAxis] = result[wideAxis];
                result[wideAxis] = swap;
            }

            return Finish(body, result[0], result[1], result[2], ranked, wall, width, thickness);
        }

        /// <summary>
        /// Width comes from the repeating cross-section (<c>area / thickness</c>) and, when the
        /// board curves hard, from the straight side edges a user would dimension. The edge
        /// reading wins when it is clearly larger: a tessellated cut of a deep curve under-reads
        /// area (160 mm panel → 131 mm), while the B-rep edges stay exact.
        /// </summary>
        private static double? TryPickWidth(
            Body2 body,
            BodyCrossSectionReader.Result section,
            double thicknessMm,
            double bboxWide,
            double lengthHint)
        {
            if (thicknessMm <= 0 || bboxWide <= 0)
            {
                return null;
            }

            double? sectionWidth = null;
            if (section != null)
            {
                var fromArea = section.AreaMm2 / thicknessMm;
                if (fromArea >= thicknessMm && fromArea <= bboxWide * MaxWidthToBoxRatio)
                {
                    sectionWidth = fromArea;
                    DiagnosticLog.Info(
                        "BodyDimensionCalculator: " + SafeName(body)
                        + " section area=" + Fmt(section.AreaMm2)
                        + " over " + section.ClusterStations + "/" + section.TotalStations + " stations"
                        + " partLength=" + Fmt(section.LengthMm)
                        + " -> width " + Fmt(fromArea) + " (box " + Fmt(bboxWide) + ")");
                }
                else
                {
                    DiagnosticLog.Info(
                        "BodyDimensionCalculator: " + SafeName(body) + " section width " + Fmt(fromArea)
                        + " implausible against box " + Fmt(bboxWide) + "/thickness " + Fmt(thicknessMm));
                }
            }

            var edgeWidth = sectionWidth.HasValue
                ? BodyStockEdgeReader.TryReadWidthMillimeters(
                    body, thicknessMm, lengthHint, section?.LengthAxis, SafeName(body))
                : null;

            var width = PickBetterWidth(sectionWidth, edgeWidth);
            if (!width.HasValue)
            {
                return null;
            }

            var change = Math.Abs(width.Value - bboxWide);
            if (change < MinWidthChangeMm || change < bboxWide * MinWidthChangeRatio)
            {
                return null;
            }

            return width;
        }

        /// <summary>
        /// A side edge only corrects the section reading, it never replaces it. On a deep
        /// cross-curve the tessellated cut loses outline and reads short, so an edge a little
        /// longer than the section is the honest width (160 against a 131 section). An edge far
        /// longer belongs to something else — the part's length, or a diagonal — and an edge with
        /// no section to check it against says nothing at all, which is how a seat that measures
        /// 453 wide came back as 378.
        /// </summary>
        private static double? PickBetterWidth(double? sectionWidth, double? edgeWidth)
        {
            if (!sectionWidth.HasValue || !edgeWidth.HasValue)
            {
                return sectionWidth;
            }

            var ratio = edgeWidth.Value / sectionWidth.Value;
            if (ratio > MinEdgeOverSectionRatio && ratio < MaxEdgeOverSectionRatio)
            {
                return edgeWidth;
            }

            return sectionWidth;
        }

        /// <summary>
        /// A body sitting at an angle to the model axes has a bounding box shorter than the part
        /// itself, because the box spreads the length over all three axes — that is how a 100 mm
        /// block came to be listed as 72 mm. The part's own length is only substituted once the
        /// box is shown to be impossible: no stock of that length could contain the solid.
        /// </summary>
        private static double? TryPickLength(
            Body2 body,
            BodyCrossSectionReader.Result section,
            double widthMm,
            double thicknessMm,
            double bboxRun,
            double? volumeMm3)
        {
            if (section == null || !section.SpineIsStraight || !volumeMm3.HasValue)
            {
                return null;
            }

            var length = section.LengthMm;
            if (length <= bboxRun || widthMm <= 0 || thicknessMm <= 0)
            {
                return null;
            }

            var boxedByBounds = bboxRun * widthMm * thicknessMm;
            if (boxedByBounds >= volumeMm3.Value * 0.98)
            {
                // The bounding-box length can still hold the solid, so leave it alone.
                return null;
            }

            var change = length - bboxRun;
            if (change < MinWidthChangeMm || change < bboxRun * MinWidthChangeRatio)
            {
                return null;
            }

            DiagnosticLog.Info(
                "BodyDimensionCalculator: " + SafeName(body) + " length " + Fmt(bboxRun)
                + " cannot hold " + Fmt(volumeMm3.Value) + " mm3, part runs " + Fmt(length));

            return length;
        }

        private static (double X, double Y, double Z,
                       DimensionAxis LengthAxis, DimensionAxis WidthAxis, DimensionAxis ThicknessAxis)
            Finish(
                Body2 body,
                double x,
                double y,
                double z,
                (int Index, double Value)[] bbox,
                double? wall,
                double? width,
                double? thickness)
        {
            var ordered = new[]
            {
                (Axis: DimensionAxis.X, Value: x),
                (Axis: DimensionAxis.Y, Value: y),
                (Axis: DimensionAxis.Z, Value: z)
            }.OrderByDescending(o => o.Value).ToArray();

            DiagnosticLog.Info(
                "BodyDimensionCalculator: " + SafeName(body)
                + " bbox=" + Fmt(bbox[0].Value) + "/" + Fmt(bbox[1].Value) + "/" + Fmt(bbox[2].Value)
                + " -> L=" + Fmt(ordered[0].Value)
                + " W=" + Fmt(ordered[1].Value)
                + " T=" + Fmt(ordered[2].Value)
                + " wall=" + Opt(wall)
                + " widthFix=" + Opt(width)
                + " thickFix=" + Opt(thickness));

            return (x, y, z, ordered[0].Axis, ordered[1].Axis, ordered[2].Axis);
        }

        private static bool IsGain(double measured, double current)
        {
            if (measured <= 0 || measured >= current)
            {
                return false;
            }

            var gain = current - measured;
            return gain >= MinAbsoluteGainMm && gain >= current * MinRelativeGain;
        }

        private static double Round(double value)
        {
            var nearestInt = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(nearestInt - value) <= 0.4)
            {
                return nearestInt;
            }

            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string Opt(double? value)
        {
            return value.HasValue ? Fmt(value.Value) : "-";
        }

        private static string Fmt(double value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string SafeName(Body2 body)
        {
            try
            {
                return body.Name ?? "?";
            }
            catch
            {
                return "?";
            }
        }
    }
}
