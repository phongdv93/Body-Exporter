using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    public sealed class BodyMetadataStore
    {
        private const string MetadataPropertyName = "SBE_BodyExportMetadata";

        public BodyExportState Load(ModelDoc2 model)
        {
            if (model == null)
            {
                return new BodyExportState();
            }

            var propertyManager = model.Extension.CustomPropertyManager[string.Empty];
            propertyManager.Get6(
                MetadataPropertyName,
                false,
                out var value,
                out var resolvedValue,
                out _,
                out _);

            var json = string.IsNullOrWhiteSpace(resolvedValue) ? value : resolvedValue;
            if (string.IsNullOrWhiteSpace(json))
            {
                return new BodyExportState();
            }

            try
            {
                return JsonConvert.DeserializeObject<BodyExportState>(json) ?? new BodyExportState();
            }
            catch
            {
                return new BodyExportState();
            }
        }

        public void Save(ModelDoc2 model, IEnumerable<BodyExportRow> rows)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            // Prune Deleted rows from the persisted metadata entirely. Earlier versions saved them
            // with LastSeenUtc=MinValue so the next scan could still surface them as a Deleted
            // warning, but in practice this meant once a body was gone its name lived in the
            // .sldprt's custom property forever, re-appearing on every future scan even after
            // the user clicked Save to acknowledge it. Dropping them on save lets Save double as
            // "acknowledge and clear" for deleted rows, which is exactly what the user asked for.
            var state = new BodyExportState
            {
                SavedAtUtc = DateTime.UtcNow,
                Bodies = rows
                    .Where(row => !row.IsDeleted)
                    .Select(row => new StoredBodyMetadata
                    {
                        PluginBodyId = row.PluginBodyId,
                        SolidWorksBodyName = row.SolidWorksBodyName,
                        DisplayName = row.DisplayName,
                        Mapping = row.GetMapping(),
                        LastKnownSize = new StoredBodySize
                        {
                            X = row.X,
                            Y = row.Y,
                            Z = row.Z
                        },
                        MaterialName = row.MaterialName,
                        ColorName = row.ColorName,
                        LastSeenUtc = DateTime.UtcNow
                    })
                    .ToList()
            };

            var json = JsonConvert.SerializeObject(state, Formatting.None);
            var propertyManager = model.Extension.CustomPropertyManager[string.Empty];
            propertyManager.Add3(
                MetadataPropertyName,
                (int)swCustomInfoType_e.swCustomInfoText,
                json,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

            model.SetSaveFlag();
        }
    }
}
