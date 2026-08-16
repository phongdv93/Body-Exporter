using System;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Reads solid volume in mm³. The mass-property array layout differs between SolidWorks
    /// releases, so candidate slots are validated against the bounding-box volume.
    /// </summary>
    internal static class BodyVolumeReader
    {
        public static double? TryReadCubicMillimeters(Body2 body, double bboxVolumeMm3)
        {
            if (body == null)
            {
                return null;
            }

            double[] mp;
            try
            {
                mp = body.GetMassProperties(1000.0) as double[];
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyVolumeReader: " + ex.Message);
                return null;
            }

            if (mp == null || mp.Length < 4)
            {
                return null;
            }

            var ceiling = bboxVolumeMm3 > 0 ? bboxVolumeMm3 * 1.05 : double.MaxValue;
            var floor = bboxVolumeMm3 > 0 ? bboxVolumeMm3 * 0.01 : 1.0;

            // Documented layout is [cx, cy, cz, volume, area, mass, ...]; probe the
            // neighbouring slots as a safety net for older interop assemblies.
            foreach (var index in new[] { 3, 1, 4 })
            {
                if (index >= mp.Length)
                {
                    continue;
                }

                var mm3 = mp[index] * 1_000_000_000.0;
                if (mm3 > floor && mm3 <= ceiling)
                {
                    return mm3;
                }
            }

            return null;
        }
    }
}
