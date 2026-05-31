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
            var bodyObjects = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true) ?? Array.Empty<object>();

            foreach (var bodyObject in bodyObjects)
            {
                var body = (Body2)bodyObject;
                var bodyName = body.Name;
                seenBodyNames.Add(bodyName);
                // Remember this name was alive at least once in the current SolidWorks process.
                // The Deleted-row emission below uses this to suppress historical noise from
                // bodies that vanished in some prior session (or were renamed away).
                SessionBodyTracker.MarkAlive(bodyName);

                var size = ReadBodySizeMillimeters(body);
                var stored = storedByBodyName.TryGetValue(bodyName, out var storedMetadata) ? storedMetadata : null;
                var mapping = stored?.Mapping ?? BodyDimensionService.CreateDefaultMapping(size.X, size.Y, size.Z);
                var status = stored == null
                    ? BodyRowStatus.New
                    : BodyDimensionService.IsSizeChanged(stored.LastKnownSize, size.X, size.Y, size.Z)
                        ? BodyRowStatus.SizeChanged
                        : BodyRowStatus.Unchanged;

                var appearance = appearanceReader.Read(body);
                rows.Add(new BodyExportRow
                {
                    PluginBodyId = stored?.PluginBodyId ?? Guid.NewGuid().ToString("N"),
                    SolidWorksBodyName = bodyName,
                    DisplayName = string.IsNullOrWhiteSpace(stored?.DisplayName) ? bodyName : stored.DisplayName,
                    X = size.X,
                    Y = size.Y,
                    Z = size.Z,
                    LengthAxis = mapping.LengthAxis,
                    WidthAxis = mapping.WidthAxis,
                    ThicknessAxis = mapping.ThicknessAxis,
                    MaterialName = ReadBodyMaterial(body, activeConfigName),
                    ColorName = appearance?.ColorName ?? ReadBodyColor(body),
                    TextureName = appearance?.TextureName,
                    ColorHex = appearance?.ColorHex,
                    Quantity = 1,
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
        private static IEnumerable<BodyExportRow> GroupIdenticalRows(List<BodyExportRow> rows)
        {
            var deleted = rows.Where(r => r.Status == BodyRowStatus.Deleted).ToList();
            var live = rows.Where(r => r.Status != BodyRowStatus.Deleted).ToList();

            var grouped = live
                .GroupBy(BuildGroupKey)
                .Select(g =>
                {
                    // Keep the alphabetically-first body as the representative so the ordering is
                    // stable across scans even when SolidWorks returns the body list in a slightly
                    // different order between sessions.
                    var representative = g.OrderBy(r => r.SolidWorksBodyName, StringComparer.OrdinalIgnoreCase).First();
                    representative.Quantity = g.Count();
                    representative.GroupMemberBodyNames = g
                        .Select(r => r.SolidWorksBodyName)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return representative;
                });

            return grouped.Concat(deleted);
        }

        private static string BuildGroupKey(BodyExportRow row)
        {
            // Sort the raw bounding-box dimensions before hashing so that two bodies which are
            // congruent but rotated (e.g. one with X=Length and another with Y=Length) still hash
            // to the same group. Round to 2 decimal places (0.01 mm) to absorb tessellation noise
            // from GetBodyBox.
            var dims = new[] { row.X, row.Y, row.Z }
                .Select(v => Math.Round(v, 2))
                .OrderBy(v => v)
                .ToArray();

            return string.Join(
                "|",
                dims[0].ToString("F2", CultureInfo.InvariantCulture),
                dims[1].ToString("F2", CultureInfo.InvariantCulture),
                dims[2].ToString("F2", CultureInfo.InvariantCulture),
                (row.MaterialName ?? string.Empty).Trim().ToUpperInvariant(),
                (row.TextureName ?? string.Empty).Trim().ToUpperInvariant(),
                (row.ColorHex ?? string.Empty).Trim().ToUpperInvariant());
        }

        public void SaveNamesToSolidWorks(ModelDoc2 model, IEnumerable<BodyExportRow> rows)
        {
            var part = (PartDoc)model;
            var bodyArray = ((object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true) ?? Array.Empty<object>())
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
            var box = (double[])body.GetBodyBox();
            var x = ToMillimeters(Math.Abs(box[3] - box[0]));
            var y = ToMillimeters(Math.Abs(box[4] - box[1]));
            var z = ToMillimeters(Math.Abs(box[5] - box[2]));

            var wall = BodyProfileThicknessReader.TryReadWallThicknessMillimeters(body);
            if (wall.HasValue)
            {
                var adjusted = BodyProfileThicknessReader.AdjustBoundingSizeForCurvedProfile(x, y, z, wall.Value);
                x = adjusted.X;
                y = adjusted.Y;
                z = adjusted.Z;
            }

            var profileWidth = BodyProfileWidthReader.TryMeasureMaxCrossSectionWidthMillimeters(body, x, y, z);
            if (profileWidth.HasValue)
            {
                var widthAxis = BodyProfileWidthReader.MiddleAxisIndex(x, y, z);
                var replaced = BodyProfileWidthReader.ReplaceAxisMillimeters(x, y, z, widthAxis, profileWidth.Value);
                x = replaced.X;
                y = replaced.Y;
                z = replaced.Z;
            }

            return new StoredBodySize { X = x, Y = y, Z = z };
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
