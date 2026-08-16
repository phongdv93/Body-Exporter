using System;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Shape traits that tell two bodies apart when their stock dimensions are identical, and that
    /// stay equal when the same body is rotated or mirrored.
    ///
    /// <para>
    /// Four corner gussets cut from 100×55×20 stock share every dimension, yet two are mitred at
    /// 40° and two at 50°. Volume separates them (89 194 mm³ against 86 691 mm³, a 2.9 % gap)
    /// while the two copies of each angle agree to a millionth. Volume alone is not enough for a
    /// drilled hole: a Ø3 hole in a seat removes 0.03 % of the solid, far below any tolerance that
    /// survives tessellation noise. A hole always adds one face and two inner loops, so the
    /// topology counts catch what the volume misses.
    /// </para>
    /// </summary>
    internal sealed class BodyShapeSignature
    {
        private BodyShapeSignature(double? volumeMm3, int faceCount, int innerLoopCount)
        {
            VolumeMm3 = volumeMm3;
            FaceCount = faceCount;
            InnerLoopCount = innerLoopCount;
        }

        /// <summary>Solid volume, or null when SolidWorks would not report a usable figure.</summary>
        public double? VolumeMm3 { get; }

        public int FaceCount { get; }

        /// <summary>Loops that bound a hole or a pocket rather than the outside of a face.</summary>
        public int InnerLoopCount { get; }

        public static BodyShapeSignature Read(Body2 body)
        {
            if (body == null)
            {
                return new BodyShapeSignature(null, 0, 0);
            }

            var volume = BodyVolumeReader.TryReadCubicMillimeters(body, ReadBoxVolumeMm3(body));
            CountTopology(body, out var faces, out var innerLoops);
            return new BodyShapeSignature(volume, faces, innerLoops);
        }

        /// <summary>
        /// Volume of the axis-aligned box, which <see cref="BodyVolumeReader"/> needs to recognise
        /// the volume slot in the mass-property array. The raw box is used rather than the
        /// corrected stock size: a corrected size can sit a hair under the true solid volume, and
        /// the reader would then reject its own answer.
        /// </summary>
        private static double ReadBoxVolumeMm3(Body2 body)
        {
            double[] box;
            try
            {
                box = body.GetBodyBox() as double[];
            }
            catch
            {
                return 0;
            }

            if (box == null || box.Length < 6)
            {
                return 0;
            }

            var x = Math.Abs(box[3] - box[0]) * 1000.0;
            var y = Math.Abs(box[4] - box[1]) * 1000.0;
            var z = Math.Abs(box[5] - box[2]) * 1000.0;
            return x * y * z;
        }

        private static void CountTopology(Body2 body, out int faceCount, out int innerLoopCount)
        {
            faceCount = 0;
            innerLoopCount = 0;

            object[] faceObjects;
            try
            {
                faceObjects = body.GetFaces() as object[];
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyShapeSignature.faces: " + ex.Message);
                return;
            }

            if (faceObjects == null)
            {
                return;
            }

            foreach (var faceObject in faceObjects)
            {
                if (!(faceObject is Face2 face))
                {
                    continue;
                }

                faceCount++;

                object[] loopObjects;
                try
                {
                    loopObjects = face.GetLoops() as object[];
                }
                catch
                {
                    continue;
                }

                if (loopObjects == null)
                {
                    continue;
                }

                foreach (var loopObject in loopObjects)
                {
                    if (!(loopObject is Loop2 loop))
                    {
                        continue;
                    }

                    try
                    {
                        if (!loop.IsOuter())
                        {
                            innerLoopCount++;
                        }
                    }
                    catch
                    {
                        // A loop that will not say which side it bounds is left uncounted rather
                        // than guessed at; guessing would split a row that should stay merged.
                    }
                }
            }
        }
    }
}
