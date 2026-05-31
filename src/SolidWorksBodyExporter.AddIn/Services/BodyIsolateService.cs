using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>Focuses one solid body in the active part (hide others, zoom to selection).</summary>
    internal static class BodyIsolateService
    {
        private const int IsolateCommandId = 1533;

        public static bool TryIsolateBody(ISldWorks swApp, ModelDoc2 model, string solidWorksBodyName)
        {
            if (swApp == null || model == null || string.IsNullOrWhiteSpace(solidWorksBodyName))
            {
                return false;
            }

            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                return false;
            }

            try
            {
                var title = model.GetTitle();
                if (!string.IsNullOrEmpty(title))
                {
                    var errors = 0;
                    swApp.ActivateDoc3(title, true, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
                }

                var part = (PartDoc)model;
                var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length == 0)
                {
                    return false;
                }

                var foundTarget = false;
                foreach (var bodyObj in bodies)
                {
                    if (!(bodyObj is Body2 body))
                    {
                        continue;
                    }

                    var isTarget = string.Equals(body.Name, solidWorksBodyName, StringComparison.OrdinalIgnoreCase);
                    if (isTarget)
                    {
                        foundTarget = true;
                        body.HideBody(false);
                    }
                    else
                    {
                        body.HideBody(true);
                    }
                }

                if (!foundTarget)
                {
                    return false;
                }

                model.ClearSelection2(true);
                model.Extension?.SelectByID2(
                    solidWorksBodyName,
                    "SOLIDBODY",
                    0,
                    0,
                    0,
                    false,
                    0,
                    null,
                    (int)swSelectOption_e.swSelectOptionDefault);

                try
                {
                    model.Extension?.RunCommand(IsolateCommandId, string.Empty);
                }
                catch
                {
                    // Hide/show already applied.
                }

                model.ViewZoomToSelection();
                model.GraphicsRedraw2();
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyIsolateService: " + ex.Message);
                return false;
            }
        }
    }
}
