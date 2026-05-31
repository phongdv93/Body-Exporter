using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Shows one solid body by hiding the rest. Uses <see cref="PartDoc.GetBodies2"/> with
    /// visibleOnly=false so hidden bodies stay enumerable (they are not deleted).
    /// </summary>
    internal static class BodyIsolateService
    {
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
                EnsurePartActive(swApp, model);

                var part = (PartDoc)model;
                var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length == 0)
                {
                    return false;
                }

                Body2 target = null;
                foreach (var bodyObj in bodies)
                {
                    if (!(bodyObj is Body2 body))
                    {
                        continue;
                    }

                    var isTarget = string.Equals(body.Name, solidWorksBodyName, StringComparison.OrdinalIgnoreCase);
                    if (isTarget)
                    {
                        target = body;
                    }

                    body.HideBody(!isTarget);
                }

                if (target == null)
                {
                    return false;
                }

                ZoomToBody(model, target);
                model.GraphicsRedraw2();
                Win32Native.TryFocusSolidWorks(swApp);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyIsolateService: " + ex.Message);
                return false;
            }
        }

        /// <summary>Restores visibility for every solid body in the active part.</summary>
        public static void ShowAllBodies(ModelDoc2 model)
        {
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                return;
            }

            try
            {
                var part = (PartDoc)model;
                var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null)
                {
                    return;
                }

                foreach (var bodyObj in bodies)
                {
                    if (bodyObj is Body2 body)
                    {
                        body.HideBody(false);
                    }
                }

                model.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyIsolateService.ShowAllBodies: " + ex.Message);
            }
        }

        private static void EnsurePartActive(ISldWorks swApp, ModelDoc2 model)
        {
            try
            {
                var active = swApp.ActiveDoc as ModelDoc2;
                if (active != null
                    && string.Equals(active.GetTitle(), model.GetTitle(), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var title = model.GetTitle();
                if (string.IsNullOrEmpty(title))
                {
                    return;
                }

                var errors = 0;
                swApp.ActivateDoc3(title, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("BodyIsolateService.EnsurePartActive: " + ex.Message);
            }
        }

        private static void ZoomToBody(ModelDoc2 model, Body2 body)
        {
            try
            {
                model.ClearSelection2(true);
                model.Extension?.SelectByID2(
                    body.Name,
                    "SOLIDBODY",
                    0,
                    0,
                    0,
                    false,
                    0,
                    null,
                    (int)swSelectOption_e.swSelectOptionDefault);
                model.ViewZoomToSelection();
            }
            catch
            {
                try
                {
                    model.ViewZoomtofit2();
                }
                catch
                {
                    // Non-fatal.
                }
            }
        }
    }
}
