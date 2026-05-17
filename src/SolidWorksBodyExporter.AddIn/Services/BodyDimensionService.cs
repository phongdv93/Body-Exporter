using System.Collections.Generic;
using System.Linq;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    public static class BodyDimensionService
    {
        public static DimensionMapping CreateDefaultMapping(double x, double y, double z)
        {
            var orderedAxes = new[]
            {
                new KeyValuePair<DimensionAxis, double>(DimensionAxis.X, x),
                new KeyValuePair<DimensionAxis, double>(DimensionAxis.Y, y),
                new KeyValuePair<DimensionAxis, double>(DimensionAxis.Z, z)
            }
            .OrderByDescending(item => item.Value)
            .ToList();

            return new DimensionMapping
            {
                LengthAxis = orderedAxes[0].Key,
                WidthAxis = orderedAxes[1].Key,
                ThicknessAxis = orderedAxes[2].Key
            };
        }

        public static bool IsSizeChanged(StoredBodySize lastKnownSize, double x, double y, double z)
        {
            if (lastKnownSize == null)
            {
                return false;
            }

            const double toleranceMillimeters = 0.01;
            return HasChanged(lastKnownSize.X, x, toleranceMillimeters)
                || HasChanged(lastKnownSize.Y, y, toleranceMillimeters)
                || HasChanged(lastKnownSize.Z, z, toleranceMillimeters);
        }

        private static bool HasChanged(double oldValue, double newValue, double tolerance)
        {
            return System.Math.Abs(oldValue - newValue) > tolerance;
        }
    }
}
