using System;
using System.Collections.Generic;

namespace SolidWorksBodyExporter.AddIn.Models
{
    public sealed class BodyExportState
    {
        public int Version { get; set; } = 1;

        public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

        public List<StoredBodyMetadata> Bodies { get; set; } = new List<StoredBodyMetadata>();
    }

    public sealed class StoredBodyMetadata
    {
        public string PluginBodyId { get; set; }

        public string SolidWorksBodyName { get; set; }

        public string DisplayName { get; set; }

        public DimensionMapping Mapping { get; set; }

        public StoredBodySize LastKnownSize { get; set; }

        public string MaterialName { get; set; }

        public string ColorName { get; set; }

        /// <summary>Detail / Hardware / Packaging (persisted as enum name or int).</summary>
        public string Category { get; set; }

        public DateTime LastSeenUtc { get; set; }
    }

    public sealed class StoredBodySize
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }
}
