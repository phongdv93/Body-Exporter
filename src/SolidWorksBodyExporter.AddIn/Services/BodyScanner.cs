using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    public sealed class BodyScanner
    {
        private readonly BodyMetadataStore _metadataStore;

        public BodyScanner(BodyMetadataStore metadataStore)
        {
            _metadataStore = metadataStore;
        }

        public IList<BodyExportRow> Scan(ModelDoc2 model)
        {
            // Return an empty grid instead of throwing so the Body Exporter window can stay open
            // while no part is loaded yet (user is about to open a file, or switched to an
            // assembly drawing). Call sites surface a toast when an operation truly needs a part.
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                return Array.Empty<BodyExportRow>();
            }

            var part = (PartDoc)model;
            var activeConfigName = ReadActiveConfigurationName(model);
            var appearanceReader = new BodyAppearanceReader(model);
            var storedState = _metadataStore.Load(model);
            var storedByBodyName = storedState.Bodies
                .Where(item => !string.IsNullOrWhiteSpace(item.SolidWorksBodyName))
                .GroupBy(item => item.SolidWorksBodyName)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastSeenUtc).First());

            var rows = new List<BodyExportRow>();
            var seenBodyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // visibleOnly=false — include bodies hidden by preview-isolate; hidden ≠ deleted.
            var bodyObjects = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false) ?? Array.Empty<object>();

            foreach (var bodyObject in bodyObjects)
            {
                var body = (Body2)bodyObject;
                var bodyName = body.Name;
                seenBodyNames.Add(bodyName);
                SessionBodyTracker.MarkAlive(bodyName);

                // Use new dimension calculator for accurate length/width/thickness
                var dims = BodyDimensionCalculator.ComputeDimensions(body);
                var size = new StoredBodySize { X = dims.X, Y = dims.Y, Z = dims.Z };

                var stored = storedByBodyName.TryGetValue(bodyName, out var storedMetadata) ? storedMetadata : null;
                // Use computed axis mapping, or fall back to stored/default
                var lengthAxis = stored?.Mapping?.LengthAxis ?? dims.LengthAxis;
                var widthAxis = stored?.Mapping?.WidthAxis ?? dims.WidthAxis;
                var thicknessAxis = stored?.Mapping?.ThicknessAxis ?? dims.ThicknessAxis;

                var status = stored == null
                    ? BodyRowStatus.New
                    : BodyDimensionService.IsSizeChanged(stored.LastKnownSize, dims.X, dims.Y, dims.Z)
                        ? BodyRowStatus.SizeChanged
                        : BodyRowStatus.Unchanged;

                var appearance = appearanceReader.Read(body);
                var shape = BodyShapeSignature.Read(body);
                rows.Add(new BodyExportRow
                {
                    PluginBodyId = stored?.PluginBodyId ?? Guid.NewGuid().ToString("N"),
                    SolidWorksBodyName = bodyName,
                    DisplayName = string.IsNullOrWhiteSpace(stored?.DisplayName) ? bodyName : stored.DisplayName,
                    X = dims.X,
                    Y = dims.Y,
                    Z = dims.Z,
                    LengthAxis = lengthAxis,
                    WidthAxis = widthAxis,
                    ThicknessAxis = thicknessAxis,
                    MaterialName = ReadBodyMaterial(body, activeConfigName),
                    ColorName = appearance?.ColorName ?? ReadBodyColor(body),
                    TextureName = appearance?.TextureName,
                    ColorHex = appearance?.ColorHex,
                    TypeId = ResolveTypeId(stored, string.IsNullOrWhiteSpace(stored?.DisplayName) ? bodyName : stored.DisplayName, bodyName),
                    Quantity = 1,
                    VolumeMm3 = shape.VolumeMm3,
                    FaceCount = shape.FaceCount,
                    InnerLoopCount = shape.InnerLoopCount,
                    Status = status,
                    Thumbnail = BodyThumbnailRenderer.Render(body)
                });
            }

            foreach (var deleted in storedState.Bodies.Where(item =>
                         !seenBodyNames.Contains(item.SolidWorksBodyName) &&
                         // Only surface a Deleted row when we have positive evidence the body
                         // was alive at some point within the current SolidWorks process.
                         // Without this check, every metadata entry from a previous session
                         // would appear as a phantom Deleted row on the very first scan after
                         // SolidWorks starts, which the user explicitly called out as noise.
                         SessionBodyTracker.WasEverAlive(item.SolidWorksBodyName)))
            {
                rows.Add(new BodyExportRow
                {
                    PluginBodyId = deleted.PluginBodyId,
                    SolidWorksBodyName = deleted.SolidWorksBodyName,
                    DisplayName = deleted.DisplayName,
                    X = deleted.LastKnownSize?.X ?? 0,
                    Y = deleted.LastKnownSize?.Y ?? 0,
                    Z = deleted.LastKnownSize?.Z ?? 0,
                    LengthAxis = deleted.Mapping?.LengthAxis ?? DimensionAxis.X,
                    WidthAxis = deleted.Mapping?.WidthAxis ?? DimensionAxis.Y,
                    ThicknessAxis = deleted.Mapping?.ThicknessAxis ?? DimensionAxis.Z,
                    MaterialName = deleted.MaterialName,
                    ColorName = deleted.ColorName,
                    TypeId = ResolveTypeId(deleted, deleted.DisplayName, deleted.SolidWorksBodyName),
                    Quantity = 1,
                    Status = BodyRowStatus.Deleted
                });
            }

            return GroupIdenticalRows(rows).ToList();
        }

        /// <summary>
        /// Collapses bodies that are visually and dimensionally identical into a single row with
        /// <see cref="BodyExportRow.Quantity"/> set to the count. Pattern/array/mirror/split copies
        /// produced by SolidWorks all share the same bounding-box dimensions (regardless of axis
        /// orientation), material and appearance, so the user sees a single "part type" row with a
        /// quantity rather than N nearly-identical rows.
        /// <para>
        /// Deleted-status rows (i.e. bodies that existed in the stored metadata but were removed
        /// from the part since the last save) are NEVER folded into a live group - they keep their
        /// own rows so the UI can show the user which historical entries are now gone.
        /// </para>
        /// </summary>
        /// <summary>Bodies whose every dimension agrees this closely are the same stock size.</summary>
        private const double DimensionToleranceMm = 0.3;

        /// <summary>
        /// Volume agreement required of one BOM line. Two copies of the same body agree to a
        /// millionth, while a different mitre angle on the same stock shifts volume by percent, so
        /// half a percent separates the two cases with room to spare.
        /// </summary>
        private const double VolumeTolerance = 0.005;

        private static IEnumerable<BodyExportRow> GroupIdenticalRows(List<BodyExportRow> rows)
        {
            var deleted = rows.Where(r => r.Status == BodyRowStatus.Deleted).ToList();

            // Body name order makes the clustering repeatable: the same body always becomes the
            // representative its siblings are compared against, whatever order SolidWorks hands
            // the bodies over in.
            var live = rows
                .Where(r => r.Status != BodyRowStatus.Deleted)
                .OrderBy(r => r.SolidWorksBodyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var clusters = new List<List<BodyExportRow>>();
            foreach (var row in live)
            {
                var joined = false;
                foreach (var cluster in clusters)
                {
                    if (BelongsTogether(cluster[0], row))
                    {
                        cluster.Add(row);
                        joined = true;
                        break;
                    }
                }

                if (!joined)
                {
                    clusters.Add(new List<BodyExportRow> { row });
                }
            }

            var grouped = clusters.Select(cluster =>
            {
                var representative = cluster[0];
                representative.Quantity = cluster.Count;
                representative.GroupMemberBodyNames = cluster
                    .Select(r => r.SolidWorksBodyName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                LogCluster(cluster);
                return representative;
            });

            return grouped.Concat(deleted);
        }

        /// <summary>
        /// Whether two bodies belong on one BOM line.
        ///
        /// <para>
        /// Dimensions and volume are compared with a tolerance rather than rounded to a fixed
        /// number of decimals. Rounding splits on which side of a boundary a value falls, so two
        /// bodies 0.0002 mm apart could land in different rows while two bodies 0.0099 mm apart
        /// shared one. A tolerance answers the question actually being asked: how far apart are
        /// they.
        /// </para>
        ///
        /// <para>
        /// Volume and the topology counts are what separate bodies cut from the same stock:
        /// a different mitre angle changes the volume, and a drilled hole — however small — adds
        /// a face and two inner loops. Both survive rotation and mirroring, so a mirrored pair
        /// still shares its line.
        /// </para>
        /// </summary>
        private static bool BelongsTogether(BodyExportRow a, BodyExportRow b)
        {
            if (!string.Equals(Normalize(a.MaterialName), Normalize(b.MaterialName), StringComparison.Ordinal) ||
                !string.Equals(Normalize(a.TextureName), Normalize(b.TextureName), StringComparison.Ordinal) ||
                !string.Equals(Normalize(a.ColorHex), Normalize(b.ColorHex), StringComparison.Ordinal) ||
                !string.Equals(
                    BomTypesService.NormalizeId(a.TypeId),
                    BomTypesService.NormalizeId(b.TypeId),
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (a.FaceCount != b.FaceCount || a.InnerLoopCount != b.InnerLoopCount)
            {
                return false;
            }

            var left = SortedDimensions(a);
            var right = SortedDimensions(b);
            for (var i = 0; i < 3; i++)
            {
                if (Math.Abs(left[i] - right[i]) > DimensionToleranceMm)
                {
                    return false;
                }
            }

            return VolumesAgree(a.VolumeMm3, b.VolumeMm3);
        }

        /// <summary>
        /// Dimensions sorted so a body standing on a different axis than its twin still matches.
        /// </summary>
        private static double[] SortedDimensions(BodyExportRow row)
        {
            var dims = new[] { row.X, row.Y, row.Z };
            Array.Sort(dims);
            return dims;
        }

        private static bool VolumesAgree(double? a, double? b)
        {
            // An unmeasured body is grouped on its dimensions alone, as it was before volume was
            // read at all. Refusing to group it would split rows over a SolidWorks hiccup.
            if (!a.HasValue || !b.HasValue || a.Value <= 0 || b.Value <= 0)
            {
                return true;
            }

            var reference = Math.Max(a.Value, b.Value);
            return Math.Abs(a.Value - b.Value) <= reference * VolumeTolerance;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Records what every row was grouped on, so a report of "these two should have merged"
        /// can be answered by reading which figure differed.
        /// </summary>
        private static void LogCluster(List<BodyExportRow> cluster)
        {
            foreach (var row in cluster)
            {
                var dims = SortedDimensions(row);
                DiagnosticLog.Info(
                    "BodyScanner group " + cluster[0].SolidWorksBodyName
                    + " x" + cluster.Count.ToString(CultureInfo.InvariantCulture)
                    + ": " + row.SolidWorksBodyName
                    + " dims=" + Fmt(dims[2]) + "/" + Fmt(dims[1]) + "/" + Fmt(dims[0])
                    + " volume=" + (row.VolumeMm3.HasValue ? Fmt(row.VolumeMm3.Value) : "-")
                    + " faces=" + row.FaceCount.ToString(CultureInfo.InvariantCulture)
                    + " innerLoops=" + row.InnerLoopCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string Fmt(double value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string ResolveTypeId(StoredBodyMetadata stored, string displayName, string bodyName)
        {
            if (stored != null && !string.IsNullOrWhiteSpace(stored.Category))
            {
                return BomTypesService.NormalizeId(stored.Category);
            }

            return BomTypesService.MatchTypeId(displayName, bodyName) ?? BomTypeIds.Detail;
        }

        /// <summary>
        /// Axis-aligned bounding box of all solid bodies in the part, in mm, ordered
        /// Length ≥ Width ≥ Height (same convention as default body mapping).
        /// </summary>
        public static PartOverallSize ReadOverallSizeMillimeters(ModelDoc2 model)
        {
            var empty = new PartOverallSize();
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                return empty;
            }

            var part = (PartDoc)model;
            var bodyObjects = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false) ?? Array.Empty<object>();
            if (bodyObjects.Length == 0)
            {
                return empty;
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            var any = false;

            foreach (var bodyObject in bodyObjects)
            {
                if (!(bodyObject is Body2 body))
                {
                    continue;
                }

                double[] box;
                try
                {
                    box = (double[])body.GetBodyBox();
                }
                catch
                {
                    continue;
                }

                if (box == null || box.Length < 6)
                {
                    continue;
                }

                any = true;
                minX = Math.Min(minX, Math.Min(box[0], box[3]));
                minY = Math.Min(minY, Math.Min(box[1], box[4]));
                minZ = Math.Min(minZ, Math.Min(box[2], box[5]));
                maxX = Math.Max(maxX, Math.Max(box[0], box[3]));
                maxY = Math.Max(maxY, Math.Max(box[1], box[4]));
                maxZ = Math.Max(maxZ, Math.Max(box[2], box[5]));
            }

            if (!any)
            {
                return empty;
            }

            var x = ToMillimeters(Math.Abs(maxX - minX));
            var y = ToMillimeters(Math.Abs(maxY - minY));
            var z = ToMillimeters(Math.Abs(maxZ - minZ));
            var ordered = new[] { x, y, z }.OrderByDescending(v => v).ToArray();
            return new PartOverallSize
            {
                LengthMm = ordered[0],
                WidthMm = ordered[1],
                HeightMm = ordered[2]
            };
        }

        public void SaveNamesToSolidWorks(ModelDoc2 model, IEnumerable<BodyExportRow> rows)
        {
            var part = (PartDoc)model;
            var bodyArray = ((object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false) ?? Array.Empty<object>())
                .Cast<Body2>()
                .ToList();

            // GroupBy / First() instead of plain ToDictionary. SolidWorks can legitimately surface
            // two solid bodies sharing the same Name in several edge cases: the user pre-renamed
            // bodies in the FeatureManager to identical labels (SW does not always enforce
            // uniqueness, especially for bodies originating from Mirror / Pattern features whose
            // seed already had the target name), or a partial rename from a previous Save left
            // siblings with overlapping suffixes. Without the GroupBy, ToDictionary threw "An
            // item with the same key has already been added" the moment we touched the Save
            // button, which aborted the entire flow BEFORE any rename was attempted - surfacing
            // as the confusing toast the user reported even though their UI edits were valid.
            var grouped = bodyArray
                .Where(body => !string.IsNullOrEmpty(body.Name))
                .GroupBy(body => body.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bodies = grouped.ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

            var collisions = grouped.Where(g => g.Count() > 1).ToList();
            if (collisions.Count > 0)
            {
                DiagnosticLog.Warn(
                    "SaveNamesToSolidWorks: " + collisions.Count + " body-name collision(s) in part; keeping first match for each. " +
                    "Collisions: " + string.Join(", ", collisions.Select(g => g.Key + " x" + g.Count())));
            }

            var rowList = rows.ToList();
            foreach (var row in rowList.Where(item => !item.IsDeleted))
            {
                var memberCount = row.GroupMemberBodyNames?.Count ?? 0;

                // Skip the SolidWorks rename for grouped rows entirely. SolidWorks forbids two
                // bodies inside the same part from sharing a name, so renaming all N members to
                // the same display name fails on the 2nd through N-th body with a "name already
                // exists" error. We instead let every member keep its auto-generated body name
                // ("Body 2", "Body 3", ...) and rely on the metadata store to remember the shared
                // display name. This matches the user expectation that grouped identical parts
                // are renamed "once" on the UI without polluting the body tree with N copies of
                // the same suffix-mangled name.
                if (memberCount > 1)
                {
                    continue;
                }

                if (!bodies.TryGetValue(row.SolidWorksBodyName, out var body))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.DisplayName) || string.Equals(body.Name, row.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    body.Name = row.DisplayName;
                    row.SolidWorksBodyName = row.DisplayName;
                }
                catch (Exception ex)
                {
                    // SolidWorks throws when the target name collides with a sibling body. Keep
                    // the original name and let the metadata store carry the user-edited
                    // DisplayName so the UI still renders the new label on the next scan.
                    DiagnosticLog.Warn(
                        "Body rename rejected by SolidWorks for " + body.Name + " -> " + row.DisplayName + ": " + ex.Message);
                }
            }

            // Reuse the PluginBodyId persisted on disk for each underlying body so each grouped
            // member keeps a stable internal identity across saves. Without this, every save
            // would mint a new GUID for the non-representative members and break any future
            // feature that wants to link external data (e.g. PDM, ERP) by PluginBodyId.
            var storedById = _metadataStore.Load(model)
                .Bodies
                .Where(b => !string.IsNullOrWhiteSpace(b.SolidWorksBodyName))
                .GroupBy(b => b.SolidWorksBodyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(b => b.LastSeenUtc).First().PluginBodyId,
                    StringComparer.OrdinalIgnoreCase);

            _metadataStore.Save(model, ExpandGroupsForMetadata(rowList, storedById));
        }

        /// <summary>
        /// Expands every grouped row back into one synthetic row per underlying body name so the
        /// metadata store records the user's <see cref="BodyExportRow.DisplayName"/>, mapping and
        /// material against EVERY body in the group. Without this expansion, only the
        /// representative body would carry the new metadata and on the next scan the other group
        /// members would fall back to their auto-generated SolidWorks names, breaking the
        /// "rename once for the whole group" UX the user asked for.
        /// </summary>
        private static IEnumerable<BodyExportRow> ExpandGroupsForMetadata(
            IList<BodyExportRow> rows,
            IDictionary<string, string> existingPluginBodyIdsByName)
        {
            foreach (var row in rows)
            {
                var members = row.GroupMemberBodyNames;
                if (members == null || members.Count <= 1)
                {
                    yield return row;
                    continue;
                }

                foreach (var memberName in members)
                {
                    if (string.Equals(memberName, row.SolidWorksBodyName, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return row;
                        continue;
                    }

                    // Reuse the persisted PluginBodyId for this member body when one exists, so
                    // the body retains its stable identity across saves. Mint a fresh GUID only
                    // for bodies the metadata store has never seen before.
                    var pluginBodyId = existingPluginBodyIdsByName.TryGetValue(memberName, out var existingId)
                        ? existingId
                        : Guid.NewGuid().ToString("N");

                    yield return new BodyExportRow
                    {
                        PluginBodyId = pluginBodyId,
                        SolidWorksBodyName = memberName,
                        DisplayName = row.DisplayName,
                        X = row.X,
                        Y = row.Y,
                        Z = row.Z,
                        LengthAxis = row.LengthAxis,
                        WidthAxis = row.WidthAxis,
                        ThicknessAxis = row.ThicknessAxis,
                        MaterialName = row.MaterialName,
                        ColorName = row.ColorName,
                        TextureName = row.TextureName,
                        ColorHex = row.ColorHex,
                        Quantity = 1,
                        Status = row.Status
                    };
                }
            }
        }

        private static StoredBodySize ReadBodySizeMillimeters(Body2 body)
        {
            var dims = BodyDimensionCalculator.ComputeDimensions(body);
            return new StoredBodySize { X = dims.X, Y = dims.Y, Z = dims.Z };
        }

        public static (double X, double Y, double Z, DimensionAxis LengthAxis, DimensionAxis WidthAxis, DimensionAxis ThicknessAxis)
            ComputeBodyDimensions(Body2 body)
        {
            return BodyDimensionCalculator.ComputeDimensions(body);
        }

        private static double ToMillimeters(double meters)
        {
            return Math.Round(meters * 1000.0, 3);
        }

        private static string ReadActiveConfigurationName(ModelDoc2 model)
        {
            try
            {
                var configuration = model.GetActiveConfiguration() as Configuration;
                return configuration?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadBodyMaterial(Body2 body, string activeConfigName)
        {
            // SolidWorks exposes per-body material through several overlapping APIs depending on the
            // SolidWorks version, whether the material is per-configuration, and whether it lives in
            // a custom database. Walk the candidates from most informative (user-visible name in the
            // active configuration) to least, and return the first non-empty result.
            return FirstNonEmpty(
                () => TryReadMaterialByConfig(body, activeConfigName),
                () => Safe(() => body.GetMaterialUserName2()),
                () => Safe(() => body.GetMaterialIdName2()),
                () => Safe(() => body.GetMaterialUserName()),
                () => Safe(() => body.GetMaterialIdName()),
                () => TryReadMaterialByConfig(body, string.Empty));
        }

        private static string TryReadMaterialByConfig(Body2 body, string configurationName)
        {
            try
            {
                var database = string.Empty;
                var material = body.GetMaterialPropertyName(configurationName, out database);
                return string.IsNullOrWhiteSpace(material) ? string.Empty : material;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                var value = read();
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FirstNonEmpty(params Func<string>[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var value = candidate();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return string.Empty;
        }

        private static string ReadBodyColor(Body2 body)
        {
            try
            {
                var values = (double[])body.MaterialPropertyValues2;
                if (values == null || values.Length < 3)
                {
                    return string.Empty;
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "RGB({0},{1},{2})",
                    ToColor(values[0]),
                    ToColor(values[1]),
                    ToColor(values[2]));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int ToColor(double value)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(value * 255)));
        }
    }
}
