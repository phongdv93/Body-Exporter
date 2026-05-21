using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksBodyExporter.AddIn.Models;
using SolidWorksBodyExporter.AddIn.Services;
using SolidWorksBodyExporter.AddIn.Services.Api;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    // WPF generates the partial class file from the .xaml under InitializeComponent(); the XAML
    // markup wires events and template selectors to method names that MUST survive any rename
    // pass, or the BAML loader will throw at runtime. Same goes for the ExportColumn enum below
    // (referenced by name in code paths that use Newtonsoft.Json or reflection).
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class BodyExportWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Currently bound Part document. Mutable because the user can switch between several
        /// open parts via the "Active part" dropdown in the toolbar without closing the window.
        /// Save / Refresh / Export operations all target whichever document <c>_model</c> points
        /// at when the user clicks the button - never the SolidWorks <c>ActiveDoc</c> at that
        /// instant. This decoupling is the whole point of the dropdown: the user can pull up
        /// Part A in the BE window, click around inside Part B in SolidWorks itself, and then
        /// click Save in BE - the save still goes to Part A as displayed in the window, not to
        /// Part B which only happens to be focused in SW at click time.
        /// </summary>
        private ModelDoc2 _model;

        /// <summary>
        /// Captured at construction so the dropdown can re-enumerate every open ModelDoc2 the
        /// user has loaded in SolidWorks. We do NOT call <c>ISldWorks.ActiveDoc</c> from here -
        /// that property reflects whichever document SW thinks is foreground, which can race
        /// with the user clicking around between parts.
        /// </summary>
        private readonly ISldWorks _solidWorks;
        private readonly BodyScanner _scanner;
        private readonly BodyMetadataStore _metadataStore;
        private readonly ExcelExporter _excelExporter;

        /// <summary>
        /// Suppresses the SelectedActivePart change handler while we rebuild the dropdown items
        /// from <see cref="RefreshOpenParts"/>. Without this guard the SelectedItem setter would
        /// observe a transient null during the rebuild and try to re-scan an empty document,
        /// which throws "Open a SolidWorks part" mid-refresh.
        /// </summary>
        private bool _suppressActivePartChange;


        private Popup _previewPopup;
        private Image _previewPopupImage;
        private DispatcherTimer _previewCloseTimer;

        /// <summary>Single synthetic "Open part…" row reused across refreshes so the ComboBox
        /// selection stays stable and the row always triggers the file dialog.</summary>
        private OpenPartItem _openPartCommandSingleton;

        public BodyExportWindow(ISldWorks solidWorks, ModelDoc2 initialModel)
        {
            InitializeComponent();

            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
            _model = initialModel;
            _metadataStore = new BodyMetadataStore();
            _scanner = new BodyScanner(_metadataStore);
            _excelExporter = new ExcelExporter();

            Rows = new ObservableCollection<BodyExportRow>();
            OpenParts = new ObservableCollection<OpenPartItem>();
            DataContext = this;

            ShowInTaskbar = true;
            WindowState = WindowState.Normal;

            InitPreviewPopup();
            SolidWorksDocumentEvents.DocumentsMayHaveChanged += OnSolidWorksDocumentsMayHaveChanged;
            Closed += BodyExportWindow_Closed;

            Loaded += BodyExportWindow_Loaded;

            RefreshOpenParts();
            RefreshRows();
            RefreshLicenseBadge();
        }

        private void BodyExportWindow_Closed(object sender, EventArgs e)
        {
            SolidWorksDocumentEvents.DocumentsMayHaveChanged -= OnSolidWorksDocumentsMayHaveChanged;
            Closed -= BodyExportWindow_Closed;
        }

        private void OnSolidWorksDocumentsMayHaveChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(OnSolidWorksDocumentsMayHaveChanged));
                return;
            }

            try
            {
                RefreshOpenParts();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("OnSolidWorksDocumentsMayHaveChanged: " + ex.Message);
            }
        }

        private void InitPreviewPopup()
        {
            _previewPopupImage = new Image
            {
                Stretch = Stretch.Uniform,
                MaxWidth = 420,
                MaxHeight = 420,
                SnapsToDevicePixels = true,
            };

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4),
                Child = _previewPopupImage,
                SnapsToDevicePixels = true,
            };

            _previewPopup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = border,
                Placement = PlacementMode.Bottom,
                IsHitTestVisible = false,
            };

            _previewCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _previewCloseTimer.Tick += (_, __) =>
            {
                _previewCloseTimer.Stop();
                if (_previewPopup != null)
                {
                    _previewPopup.IsOpen = false;
                }
            };
        }

        private void BodyExportWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= BodyExportWindow_Loaded;
            // Hosted in SolidWorks: a real MainWindow fixes ShowDialog / CenterOwner for child dialogs.
            if (Application.Current != null)
            {
                Application.Current.MainWindow = this;
            }

            // Open empty: user picks a part from the Active part dropdown or opens a file later.
            RefreshExcelTemplateBar();
        }

        public ObservableCollection<BodyExportRow> Rows { get; }

        /// <summary>
        /// Items shown in the "Active part" dropdown above the bodies grid. Refreshed (a) at
        /// window construction, (b) every time the user clicks the toolbar Refresh button, and
        /// (c) every time the dropdown is opened so a part the user just opened in SolidWorks
        /// after the window was up still appears in the list without forcing them to close
        /// and reopen Body Exporter.
        /// </summary>
        public ObservableCollection<OpenPartItem> OpenParts { get; }

        private OpenPartItem _selectedActivePart;
        public OpenPartItem SelectedActivePart
        {
            get => _selectedActivePart;
            set
            {
                if (ReferenceEquals(_selectedActivePart, value)) return;

                if (value?.IsOpenPartCommand == true)
                {
                    _selectedActivePart = value;
                    OnPropertyChanged();
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(() =>
                        {
                            if (TryOpenPartFromDiskCore(out var opened))
                            {
                                SwitchActivePart(opened);
                            }

                            RefreshOpenParts();
                        }));
                    return;
                }

                _selectedActivePart = value;
                OnPropertyChanged();
                if (_suppressActivePartChange || value?.Model == null) return;
                SwitchActivePart(value.Model);
            }
        }

        /// <summary>
        /// Bound to the window title so the user can confirm at a glance:
        /// (a) which build of the add-in is currently loaded - critical when bouncing between
        ///     rebuilds during diagnosis, and
        /// (b) WHICH SolidWorks part file the window is operating on. When the user switches
        ///     parts via the "Active part" dropdown, this property re-fires so the OS title
        ///     bar updates in real time - matching the visible row data the user just selected.
        /// </summary>
        public string WindowTitle
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
                var partName = SafeGetActivePartFileName(_model);
                return string.IsNullOrEmpty(partName)
                    ? "SolidWorks Body Exporter v" + version
                    : "SolidWorks Body Exporter v" + version + " - " + partName;
            }
        }

        /// <summary>
        /// Re-binds <see cref="_model"/> to a different open Part document and reloads the
        /// bodies grid against it. The dropdown SelectedItem setter routes here. Save and
        /// Export buttons consume <see cref="_model"/> directly so they automatically follow
        /// whichever part the user picked - no separate "save to selected" wiring needed.
        /// </summary>
        private void SwitchActivePart(ModelDoc2 model)
        {
            try
            {
                _model = model;
                OnPropertyChanged(nameof(WindowTitle));
                RefreshRows();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("SwitchActivePart failed", ex);
                ShowToast("Could not switch part: " + ex.Message, ToastKind.Error);
            }
        }

        /// <summary>
        /// Walks <see cref="ISldWorks.GetDocuments"/> and rebuilds <see cref="OpenParts"/>
        /// with every Part-type ModelDoc2 currently loaded. Drawings and Assemblies are
        /// filtered out because the bodies grid only knows how to read PartDoc surfaces.
        /// <para>
        /// Preserves the current selection by matching on file path; if the previously
        /// selected part is still open it stays selected after the refresh. If it was
        /// closed, the first remaining part becomes active. A synthetic "Open part…" row
        /// is shown at the top when no parts are open, or at the bottom when one or more
        /// parts exist.
        /// </para>
        /// <para>
        /// Field assignment for <see cref="_selectedActivePart"/> during rebuild bypasses
        /// the <see cref="SelectedActivePart"/> setter, so a follow-up sync step calls
        /// <see cref="SwitchActivePart"/> when the matched row is a real part — fixing the
        /// case where Body Exporter was opened before any part existed and a part is opened
        /// later in SolidWorks.
        /// </para>
        /// </summary>
        private OpenPartItem GetOrCreateOpenPartCommandItem()
        {
            if (_openPartCommandSingleton == null)
            {
                _openPartCommandSingleton = new OpenPartItem
                {
                    IsOpenPartCommand = true,
                    Model = null,
                    DisplayName = "Open part…",
                    PathName = string.Empty,
                };
            }

            return _openPartCommandSingleton;
        }

        private void RefreshOpenParts()
        {
            try
            {
                var previouslySelectedPath = _model == null
                    ? string.Empty
                    : SafeGetPath(_model);

                var docs = _solidWorks.GetDocuments() as object[] ?? Array.Empty<object>();
                var items = new List<OpenPartItem>(docs.Length);
                foreach (var doc in docs)
                {
                    if (!(doc is ModelDoc2 md)) continue;
                    int docType;
                    try { docType = md.GetType(); } catch { continue; }
                    if (docType != (int)swDocumentTypes_e.swDocPART) continue;

                    items.Add(new OpenPartItem
                    {
                        Model = md,
                        DisplayName = SafeGetActivePartFileName(md),
                        PathName = SafeGetPath(md),
                    });
                }

                items = items
                    .OrderBy(p => p.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var openCmd = GetOrCreateOpenPartCommandItem();

                _suppressActivePartChange = true;
                try
                {
                    OpenParts.Clear();

                    if (items.Count == 0)
                    {
                        OpenParts.Add(openCmd);
                        _selectedActivePart = openCmd;
                    }
                    else
                    {
                        foreach (var item in items)
                        {
                            OpenParts.Add(item);
                        }

                        OpenParts.Add(openCmd);

                        var match = items.FirstOrDefault(p =>
                            !string.IsNullOrEmpty(p.PathName) &&
                            string.Equals(p.PathName, previouslySelectedPath, StringComparison.OrdinalIgnoreCase))
                            ?? items.FirstOrDefault(p => ReferenceEquals(p.Model, _model))
                            ?? items[0];

                        _selectedActivePart = match;
                    }

                    OnPropertyChanged(nameof(SelectedActivePart));
                }
                finally
                {
                    _suppressActivePartChange = false;
                }

                var syncTarget = _selectedActivePart;
                if (syncTarget != null && !syncTarget.IsOpenPartCommand && syncTarget.Model != null)
                {
                    if (!ReferenceEquals(_model, syncTarget.Model))
                    {
                        SwitchActivePart(syncTarget.Model);
                    }
                }
                else if (_model != null && (syncTarget == null || syncTarget.IsOpenPartCommand))
                {
                    _model = null;
                    OnPropertyChanged(nameof(WindowTitle));
                    RefreshRows();
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("RefreshOpenParts failed: " + ex.Message);
            }
        }

        private static string SafeGetPath(ModelDoc2 model)
        {
            try { return model?.GetPathName() ?? string.Empty; }
            catch { return string.Empty; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Returns the file name (without path, without extension) of the part the window is
        /// bound to, or empty when the document has not been saved yet (<see cref="ModelDoc2.GetPathName"/>
        /// returns the empty string for unsaved Untitled1 documents).
        /// </summary>
        private static string SafeGetActivePartFileName(ModelDoc2 model)
        {
            if (model == null) return string.Empty;
            try
            {
                var path = model.GetPathName();
                if (string.IsNullOrWhiteSpace(path))
                {
                    // Unsaved Untitled1 - fall back to GetTitle which still returns the in-memory
                    // document caption so the user can at least tell which Untitled doc is being
                    // exported when several are open at once.
                    var title = model.GetTitle();
                    return string.IsNullOrWhiteSpace(title) ? string.Empty : title;
                }
                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string SuggestExcelExportFileName()
        {
            var part = SafeGetActivePartFileName(_model);
            if (string.IsNullOrWhiteSpace(part))
            {
                part = "solidworks-bodies";
            }

            return SanitizeFileName(part) + ".xlsx";
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "export";
            }

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name.Trim())
            {
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            var cleaned = sb.ToString().Trim();
            return string.IsNullOrEmpty(cleaned) ? "export" : cleaned;
        }

        private void BodyExportWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                Refresh_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                RunOpenPartCommand();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                Save_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                // Inside the bodies grid (or while editing a cell): let WPF copy selection / cell text.
                // Copy All only when focus is on toolbar, chrome, etc. — avoids lag after renaming a body.
                if (IsFocusInBodiesGrid() || IsCellEditorTextBoxFocused())
                {
                    return;
                }

                CopyAll_Click(sender, e);
                e.Handled = true;
            }
        }

        private bool IsFocusInBodiesGrid()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (ReferenceEquals(focused, BodiesGrid))
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private bool IsCellEditorTextBoxFocused()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is System.Windows.Controls.TextBox tb && !tb.IsReadOnly && tb.IsKeyboardFocused)
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    RefreshOpenParts();
                    RefreshRows();
                }));
        }

        /// <summary>
        /// Fires right before the dropdown opens. Re-enumerates open SolidWorks parts so the
        /// user always sees the live document list rather than the snapshot taken at window
        /// construction. Cheap (one ISldWorks.GetDocuments call) so we can afford to run it
        /// on every open without making the dropdown feel sluggish.
        /// </summary>
        private void ActivePartCombo_DropDownOpened(object sender, EventArgs e)
        {
            RefreshOpenParts();
        }

        private void ActivePartCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_suppressActivePartChange)
            {
                return;
            }

            if (!IsOnlyOpenPartCommandInList())
            {
                return;
            }

            // One synthetic row: open file dialog on any click (arrow or label), not an empty dropdown.
            e.Handled = true;
            ActivePartCombo.IsDropDownOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(RunOpenPartCommand));
        }

        private bool IsOnlyOpenPartCommandInList()
        {
            return OpenParts.Count == 1
                   && OpenParts[0] is OpenPartItem o
                   && o.IsOpenPartCommand;
        }

        private void RunOpenPartCommand()
        {
            if (!TryOpenPartFromDiskCore(out var opened))
            {
                return;
            }

            SwitchActivePart(opened);
            RefreshOpenParts();
        }

        private void PreviewThumbnail_MouseEnter(object sender, MouseEventArgs e)
        {
            _previewCloseTimer?.Stop();
            if (_previewPopup == null || _previewPopupImage == null)
            {
                return;
            }

            if (!(sender is FrameworkElement fe) || !(fe.DataContext is BodyExportRow row) || row.Thumbnail == null)
            {
                return;
            }

            _previewPopupImage.Source = row.Thumbnail;
            _previewPopup.PlacementTarget = fe;
            _previewPopup.Placement = PlacementMode.Bottom;
            _previewPopup.VerticalOffset = 6;
            _previewPopup.HorizontalOffset = 0;
            _previewPopup.IsOpen = true;
        }

        private void PreviewThumbnail_MouseLeave(object sender, MouseEventArgs e)
        {
            _previewCloseTimer?.Start();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_model == null)
                {
                    ShowToast("Open or select a SolidWorks part first.", ToastKind.Info);
                    return;
                }

                _scanner.SaveNamesToSolidWorks(_model, Rows);
                if (TrySaveSolidWorksPartFile(_model, out var saveMsg))
                {
                    ShowToast("Saved to SolidWorks and part file", ToastKind.Success);
                }
                else
                {
                    ShowToast("Body names updated — " + saveMsg, ToastKind.Info);
                }
            }
            catch (Exception ex)
            {
                // Log to disk before showing the toast - the toast message is truncated to the
                // first exception line, and prior duplicate-body-name and metadata-collision
                // crashes were essentially undiagnosable from the toast alone. addin.log keeps
                // the full stack trace so the user can attach it to a support email.
                DiagnosticLog.Error("Save_Click failed", ex);
                ShowToast("Save failed: " + ex.Message, ToastKind.Error);
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var includeHeader = IncludeHeaderCheck.IsChecked == true;
                // Tab-separated clipboard payload has no concept of embedded images, so the
                // Preview column is always stripped before the text builder runs. Excel export
                // keeps Preview in via the same GetExportColumnOrder() helper.
                var columns = GetExportColumnOrder().Where(c => c != ExportColumn.Preview).ToList();
                var payload = BuildTabSeparatedText(Rows, columns, includeHeader);
                CopyTextToClipboardWithRetry(payload);
                var rowCount = Rows.Count;
                ShowToast(rowCount == 1
                    ? "Copied 1 row to clipboard"
                    : "Copied " + rowCount + " rows to clipboard", ToastKind.Success);
            }
            catch (Exception ex)
            {
                ShowToast("Copy failed: " + ex.Message, ToastKind.Error);
            }
        }

        /// <summary>
        /// Handles the "→ Length / → Width / → Thickness" swap buttons on every dimension cell.
        /// The button Tag carries a "<c>SourceSlot-&gt;TargetSlot</c>" specifier (e.g. "Length-&gt;Width")
        /// identifying which two slots should trade their axis assignments. After the swap the
        /// values displayed in those two columns trade places: e.g. clicking "→ Width" in the
        /// Length cell moves the number that was in Width into Length and vice versa, exactly
        /// the interaction the user described ("bấm change to width thì cái số ở width được đổi
        /// qua Length luôn").
        /// </summary>
        private void SwapAxis_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.DataContext is BodyExportRow row) || !(button.Tag is string spec))
            {
                return;
            }

            var parts = spec.Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                return;
            }

            if (!Enum.TryParse(parts[0], out DimensionSlot source) ||
                !Enum.TryParse(parts[1], out DimensionSlot target))
            {
                return;
            }

            row.SwapAxes(source, target);
        }

        /// <summary>
        /// Returns the export-relevant columns (Body Name + Length + Width + Thickness + Quantity +
        /// Appearance) in the order the user currently has them in the DataGrid. Reordering a
        /// column header via drag-and-drop therefore changes both the Copy All clipboard payload
        /// and the Excel export sheet, matching the user's request that the export "theo form
        /// trên file của người dùng" - follow the column order in their own file.
        /// </summary>
        private IReadOnlyList<ExportColumn> GetExportColumnOrder()
        {
            // DataGridColumn.DisplayIndex reflects the live column order after user drag/drop
            // reordering. We sort our subset of exportable columns by that index so the output
            // matches what the user sees on screen.
            var candidates = new List<(int Index, ExportColumn Column)>();
            foreach (var dgColumn in BodiesGrid.Columns)
            {
                if (dgColumn.Header is string header && TryMapHeaderToExportColumn(header, out var exportColumn))
                {
                    candidates.Add((dgColumn.DisplayIndex, exportColumn));
                }
            }

            return candidates
                .OrderBy(c => c.Index)
                .Select(c => c.Column)
                .ToList();
        }

        private static bool TryMapHeaderToExportColumn(string header, out ExportColumn column)
        {
            switch (header)
            {
                case "Preview":    column = ExportColumn.Preview;   return true;
                case "Body Name":  column = ExportColumn.BodyName;  return true;
                case "Length":     column = ExportColumn.Length;    return true;
                case "Width":      column = ExportColumn.Width;     return true;
                case "Thickness":  column = ExportColumn.Thickness; return true;
                case "Qty":        column = ExportColumn.Quantity;  return true;
                case "Appearance": column = ExportColumn.Appearance; return true;
                default:           column = default; return false;
            }
        }

        /// <summary>
        /// Win32 clipboard is single-writer; other apps (RDP clipboard monitor, browser, IME, etc.)
        /// hold the global handle for short windows and SetText throws CLIPBRD_E_CANT_OPEN (HRESULT
        /// 0x800401D0). We retry up to 20 times with very short delays AND pump the WPF dispatcher
        /// between attempts so any pending paste-monitor callbacks drain - that's the difference
        /// between "Office had the clipboard locked for 80ms" succeeding instead of failing.
        /// <para>
        /// Total retry budget is kept well under one second so SolidWorks never marks the add-in
        /// "unresponsive" (the threshold is around 3 seconds and would grey the ribbon button if
        /// crossed). The combination of (a) short fixed delays, (b) DispatcherFrame yields, and
        /// (c) <see cref="Clipboard.Clear"/> on the second-to-last attempt has empirically been
        /// enough to survive every "OpenClipboard Failed" report we've collected.
        /// </para>
        /// </summary>
        private static void CopyTextToClipboardWithRetry(string text)
        {
            text = text ?? string.Empty;
            const int maxAttempts = 20;
            const int delayMs = 30;
            Exception last = null;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    // On the second-to-last attempt try clearing the clipboard first. This forces
                    // any stale handle owned by us (or a previous SetText that half-completed) to
                    // be released so the next SetDataObject can take a fresh lock. We DON'T do
                    // this on every attempt because Clipboard.Clear itself can throw the same
                    // CLIPBRD_E_CANT_OPEN.
                    if (attempt == maxAttempts - 2)
                    {
                        try { Clipboard.Clear(); } catch { /* best effort */ }
                    }

                    var data = new DataObject();
                    data.SetData(DataFormats.UnicodeText, text);
                    data.SetData(DataFormats.Text, text);
                    Clipboard.SetDataObject(data, copy: true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                // Yield to the WPF dispatcher so pending messages (clipboard listeners, focus
                // notifications, etc.) drain. PushFrame returns when ExitFrame schedules itself
                // via Background priority - effectively "process whatever messages are queued
                // right now, then come back".
                PumpDispatcherOnce();
                Thread.Sleep(delayMs);
            }

            throw last ?? new InvalidOperationException("Clipboard could not be opened.");
        }

        private static void PumpDispatcherOnce()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// Shows a transient, non-blocking toast at the bottom of the window. Auto-dismisses after
        /// ~2 seconds. Used in place of a modal MessageBox for clipboard / save / refresh feedback
        /// so the user gets confirmation without losing keyboard focus or having to dismiss a popup
        /// before they can keep working.
        /// </summary>
        private void ShowToast(string message, ToastKind kind)
        {
            ToastText.Text = message;
            switch (kind)
            {
                case ToastKind.Success:
                    ToastBorder.Background = (System.Windows.Media.Brush)FindResource("ToastSuccessBrush");
                    break;
                case ToastKind.Error:
                    ToastBorder.Background = (System.Windows.Media.Brush)FindResource("ToastErrorBrush");
                    break;
                default:
                    ToastBorder.Background = (System.Windows.Media.Brush)FindResource("ToastInfoBrush");
                    break;
            }

            // Restart the fade-in/out storyboard from the beginning so consecutive copies don't get
            // stuck in mid-fade.
            var storyboard = (System.Windows.Media.Animation.Storyboard)FindResource("ToastStoryboard");
            storyboard.Stop(ToastBorder);
            storyboard.Begin(ToastBorder, true);
        }

        private void ExportMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu != null)
            {
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                b.ContextMenu.IsOpen = true;
            }
        }

        /// <summary>Template bar stays collapsed on open; user expands via Export menu or after linking a template.</summary>
        private bool _excelTemplateBarExpanded;

        private void ShowExcelTemplatePanel_Click(object sender, RoutedEventArgs e)
        {
            _excelTemplateBarExpanded = true;
            RefreshExcelTemplateBar();
        }

        private void ChooseDefaultTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel template (*.xlsx)|*.xlsx",
                Title = "Choose Excel template (.xlsx)"
            };
            if (dlg.ShowDialog(this) != true)
            {
                RefreshExcelTemplateBar();
                return;
            }

            var s = AppSettings.LoadOrCreate();
            s.ExcelTemplatePath = dlg.FileName;
            s.Save();
            _excelTemplateBarExpanded = true;
            RefreshExcelTemplateBar();
            ShowToast("Template path saved.", ToastKind.Success);
        }

        private void HideExcelTemplateBar_Click(object sender, RoutedEventArgs e)
        {
            _excelTemplateBarExpanded = false;
            SetExcelTemplateBarVisible(false);
        }

        private void OpenExcelTemplate_Click(object sender, RoutedEventArgs e)
        {
            var path = AppSettings.LoadOrCreate().ExcelTemplatePath;
            if (!TryOpenExternalFile(path, out var error))
            {
                ShowToast(error, ToastKind.Error);
                RefreshExcelTemplateBar();
            }
        }

        private void OpenLastExcelOutput_Click(object sender, RoutedEventArgs e)
        {
            var path = AppSettings.LoadOrCreate().ExcelTemplateLastOutputPath;
            if (!TryOpenExternalFile(path, out var error))
            {
                ShowToast(error, ToastKind.Error);
                RefreshExcelTemplateBar();
            }
        }

        private void SetExcelTemplateBarVisible(bool visible)
        {
            if (ExcelTemplateBar != null)
            {
                ExcelTemplateBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshExcelTemplateBar()
        {
            var settings = AppSettings.LoadOrCreate();
            var templatePath = settings.ExcelTemplatePath;
            var hasTemplate = !string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath);

            SetExcelTemplateBarVisible(_excelTemplateBarExpanded);
            UpdateExportMenuTemplateItems(hasTemplate);

            if (ExcelTemplatePathText != null)
            {
                if (hasTemplate)
                {
                    ExcelTemplatePathText.Text = Path.GetFileName(templatePath);
                    ExcelTemplatePathText.ToolTip = templatePath;
                }
                else
                {
                    ExcelTemplatePathText.Text = "(none)";
                    ExcelTemplatePathText.ToolTip = "No template linked yet — click Add new.";
                }
            }

            if (OpenExcelTemplateButton != null)
            {
                OpenExcelTemplateButton.IsEnabled = hasTemplate;
            }

            if (ChooseTemplateButton != null)
            {
                ChooseTemplateButton.Content = hasTemplate ? "Change…" : "Add new";
            }
        }

        private void UpdateExportMenuTemplateItems(bool hasTemplate)
        {
            var lastPath = AppSettings.LoadOrCreate().ExcelTemplateLastOutputPath;
            var hasLast = !string.IsNullOrWhiteSpace(lastPath) && File.Exists(lastPath);
            if (OpenLastExcelOutputMenuItem != null)
            {
                OpenLastExcelOutputMenuItem.Visibility = hasLast ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static bool TryOpenExternalFile(string path, out string error)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "File not found.";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("TryOpenExternalFile failed: " + path, ex);
                error = "Could not open file: " + ex.Message;
                return false;
            }
        }

        private void TryOpenPartFromDisk()
        {
            if (!TryOpenPartFromDiskCore(out var doc))
            {
                return;
            }

            SwitchActivePart(doc);
            RefreshOpenParts();
        }

        private bool TryOpenPartFromDiskCore(out ModelDoc2 opened)
        {
            opened = null;
            var dlg = new OpenFileDialog
            {
                Filter = "SolidWorks Part (*.sldprt)|*.sldprt|All files (*.*)|*.*",
                Title = "Choose a SolidWorks part file"
            };
            if (dlg.ShowDialog(this) != true)
            {
                return false;
            }

            try
            {
                var errors = 0;
                var warnings = 0;
                var doc = _solidWorks.OpenDoc6(
                    dlg.FileName,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;

                if (doc == null)
                {
                    ShowToast("SolidWorks could not open that file (error code " + errors + ").", ToastKind.Error);
                    return false;
                }

                opened = doc;
                ShowToast("Part opened.", ToastKind.Success);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("TryOpenPartFromDiskCore failed", ex);
                ShowToast("Open failed: " + ex.Message, ToastKind.Error);
                return false;
            }
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = SuggestExcelExportFileName()
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                // Excel export keeps the Preview column when it is present in the grid order:
                // ExcelExporter sees it and switches to image embedding for that cell. The
                // user toggles Preview inclusion via the "Include preview images" checkbox in
                // the toolbar (default on).
                var includePreview = IncludePreviewImagesCheck.IsChecked == true;
                var columns = GetExportColumnOrder()
                    .Where(c => c != ExportColumn.Preview || includePreview)
                    .ToList();

                _excelExporter.Export(dialog.FileName, Rows, columns);
                ShowToast("Excel file exported" + (includePreview ? " with previews" : string.Empty), ToastKind.Success);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("ExportExcel_Click failed", ex);
                ShowToast("Export failed: " + ex.Message, ToastKind.Error);
            }
        }

        /// <summary>
        /// Template-fill export. User picks an .xlsx template that contains
        /// <c>{{Placeholder}}</c> markers in the cells they want filled (Body Name,
        /// Length, Width, Thickness, Quantity, Material, Appearance, Preview). The
        /// addin clones the template to a destination path, replaces every placeholder
        /// with the first body's data, and overflows additional bodies into the rows
        /// below the placeholder row.
        /// <para>
        /// Unknown placeholder keys are surfaced via toast so the user can correct their
        /// template - we deliberately do NOT throw on unknown keys because most
        /// templates also contain unrelated double-brace strings (e.g. a literal
        /// "{{COMPANY_LOGO_HERE}}" annotation) that should be left alone.
        /// </para>
        /// </summary>
        private void ExportToTemplate_Click(object sender, RoutedEventArgs e)
        {
            string templatePath;
            var settings = AppSettings.LoadOrCreate();
            if (!string.IsNullOrWhiteSpace(settings.ExcelTemplatePath) && File.Exists(settings.ExcelTemplatePath))
            {
                templatePath = settings.ExcelTemplatePath;
            }
            else
            {
                var openDialog = new OpenFileDialog
                {
                    Filter = "Excel Template (*.xlsx)|*.xlsx",
                    Title = "Pick the Excel template to fill",
                    CheckFileExists = true
                };
                if (openDialog.ShowDialog(this) != true)
                {
                    return;
                }

                templatePath = openDialog.FileName;
                if (string.IsNullOrWhiteSpace(settings.ExcelTemplatePath))
                {
                    settings.ExcelTemplatePath = templatePath;
                    settings.Save();
                    RefreshExcelTemplateBar();
                }
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = SuggestExcelExportFileName(),
                Title = "Save filled workbook as..."
            };
            if (saveDialog.ShowDialog(this) != true) return;

            try
            {
                var exporter = new ExcelTemplateExporter();
                var result = exporter.Export(templatePath, saveDialog.FileName, Rows);

                var msg = "Template filled with " + result.RowsWritten + " bodies";
                if (result.UnknownPlaceholders != null && result.UnknownPlaceholders.Count > 0)
                {
                    msg += " (unknown placeholders skipped: " + string.Join(", ", result.UnknownPlaceholders) + ")";
                    ShowToast(msg, ToastKind.Info);
                }
                else
                {
                    ShowToast(msg, ToastKind.Success);
                }

                settings.ExcelTemplateLastOutputPath = saveDialog.FileName;
                settings.Save();
                _excelTemplateBarExpanded = true;
                RefreshExcelTemplateBar();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("ExportToTemplate_Click failed", ex);
                ShowToast("Template export failed: " + ex.Message, ToastKind.Error);
            }
        }

        private void EditConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is BodyExportRow row)
            {
                row.IsEditing = !row.IsEditing;
            }
        }

        private void SuggestSort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Rows.Count == 0)
                {
                    ShowToast("No bodies to sort.", ToastKind.Info);
                    return;
                }

                var service = BodySortRulesService.LoadFromUserSettings();
                var snapshot = Rows.ToList();
                var analysis = service.Analyze(snapshot);
                var sorted = service.Sort(snapshot);

                Rows.Clear();
                foreach (var row in sorted)
                {
                    Rows.Add(row);
                }

                if (analysis.HasIssues)
                {
                    var preview = string.Join("; ", analysis.OutOfOrder.Take(2));
                    if (analysis.OutOfOrder.Count > 2)
                    {
                        preview += " …";
                    }

                    ShowToast(
                        "Sorted by rules. " + analysis.OutOfOrder.Count + " row(s) were out of suggested order: " + preview,
                        ToastKind.Info);
                }
                else
                {
                    ShowToast("Sorted by keyword rules — order looks good.", ToastKind.Success);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Suggest order failed: " + ex.Message, ToastKind.Error);
            }
        }

        private void MoveRowUp_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is BodyExportRow row))
            {
                return;
            }

            var i = Rows.IndexOf(row);
            if (i <= 0)
            {
                return;
            }

            Rows.Move(i, i - 1);
        }

        private void MoveRowDown_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is BodyExportRow row))
            {
                return;
            }

            var i = Rows.IndexOf(row);
            if (i < 0 || i >= Rows.Count - 1)
            {
                return;
            }

            Rows.Move(i, i + 1);
        }

        /// <summary>
        /// Persists the part to disk after in-memory body renames. Skips when the document has
        /// never been saved (no path) — user must Save As in SolidWorks first.
        /// </summary>
        private static bool TrySaveSolidWorksPartFile(ModelDoc2 model, out string userMessage)
        {
            userMessage = string.Empty;
            if (model == null)
            {
                userMessage = "no active document";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.GetPathName()))
            {
                userMessage =
                    "part not on disk yet — use File > Save As in SolidWorks, then Save to SolidWorks again.";
                return false;
            }

            var err = 0;
            var warn = 0;
            // ModelDoc2.Save3: swSaveOptions_Silent = 1 (some interop builds omit swSaveOptions_e)
            model.Save3(1, ref err, ref warn);
            if (err != 0)
            {
                userMessage = "SolidWorks Save failed (error " + err + "). Save the part manually (Ctrl+S).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// License pill click. Defer ShowDialog: calling it synchronously from a mouse event in a
        /// host process (SolidWorks) often leaves the dialog invisible or behind the SW frame.
        /// </summary>
        private void LicenseBadge_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ShowLicenseDialogDeferred));
        }

        private void ShowLicenseDialogDeferred()
        {
            LicenseWindow dialog = null;
            try
            {
                DiagnosticLog.Info("License: creating LicenseWindow");
                dialog = new LicenseWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                DiagnosticLog.Info("License: calling ShowDialog");
                dialog.ShowDialog();
                DiagnosticLog.Info("License: ShowDialog returned, LicenseChanged=" + dialog.LicenseChanged);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("LicenseWindow.ShowDialog failed", ex);
                ShowToast("Could not open License window: " + ex.Message, ToastKind.Error);
                return;
            }

            var changed = dialog.LicenseChanged;
            RefreshLicenseBadge();
            if (changed)
            {
                RefreshRows();
            }
        }

        /// <summary>
        /// Re-pulls the license status and updates the toolbar pill colour + label. Calls
        /// <see cref="LicenseManager.GetStatus"/> which is cheap (no I/O when state is cached) so
        /// it's fine to call from constructor and after the License dialog closes.
        /// </summary>
        private void RefreshLicenseBadge()
        {
            var status = Services.LicenseManager.Current.GetStatus();
            string text;
            System.Windows.Media.Color background;
            System.Windows.Media.Color foreground;

            switch (status.Source)
            {
                case LicenseSource.Licensed:
                    text = "Licensed" + (status.DaysRemaining.HasValue ? " • " + status.DaysRemaining + "d" : string.Empty);
                    background = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFE6F4EA");
                    foreground = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF1B5E20");
                    break;
                case LicenseSource.Trial:
                case LicenseSource.FreshTrial:
                    text = "Trial • " + (status.DaysRemaining ?? 0) + "d left";
                    background = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFF4E5");
                    foreground = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB26A00");
                    break;
                case LicenseSource.TrialExpired:
                case LicenseSource.Expired:
                    text = status.Source == LicenseSource.Expired ? "License expired" : "Trial expired";
                    background = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFDEAEA");
                    foreground = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB71C1C");
                    break;
                case LicenseSource.Tampered:
                case LicenseSource.WrongMachine:
                    text = "License invalid";
                    background = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFDEAEA");
                    foreground = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB71C1C");
                    break;
                default:
                    text = "License";
                    background = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF1F4F9");
                    foreground = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3D5A80");
                    break;
            }

            LicenseBadgeText.Text = text;
            LicenseBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(foreground);
            LicenseBadge.Background = new System.Windows.Media.SolidColorBrush(background);
        }

        private void RefreshRows()
        {
            Rows.Clear();
            foreach (var row in _scanner.Scan(_model))
            {
                Rows.Add(row);
            }
        }

        private static string BuildTabSeparatedText(ObservableCollection<BodyExportRow> rows, IReadOnlyList<ExportColumn> order, bool includeHeader)
        {
            var builder = new StringBuilder();

            if (includeHeader)
            {
                for (var i = 0; i < order.Count; i++)
                {
                    if (i > 0) builder.Append('\t');
                    builder.Append(ExportColumnHeader(order[i]));
                }
                builder.AppendLine();
            }

            foreach (var row in rows)
            {
                for (var i = 0; i < order.Count; i++)
                {
                    if (i > 0) builder.Append('\t');
                    builder.Append(ExportColumnValue(row, order[i]));
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }

        internal static string ExportColumnHeader(ExportColumn column)
        {
            switch (column)
            {
                case ExportColumn.Preview:    return "Preview";
                case ExportColumn.BodyName:   return "Body Name";
                case ExportColumn.Length:     return "Length";
                case ExportColumn.Width:      return "Width";
                case ExportColumn.Thickness:  return "Thickness";
                case ExportColumn.Quantity:   return "Quantity";
                case ExportColumn.Appearance: return "Appearance";
                default: throw new ArgumentOutOfRangeException(nameof(column), column, null);
            }
        }

        internal static string ExportColumnValue(BodyExportRow row, ExportColumn column)
        {
            switch (column)
            {
                // The Preview column carries no text payload - it surfaces as an embedded PNG
                // in the Excel writer and is silently skipped by the clipboard path. We still
                // return an empty string here so generic callers (CSV, TSV, tooltip) don't NRE.
                case ExportColumn.Preview:    return string.Empty;
                case ExportColumn.BodyName:   return row.DisplayName ?? string.Empty;
                case ExportColumn.Length:     return FormatDimension(row.Length);
                case ExportColumn.Width:      return FormatDimension(row.Width);
                case ExportColumn.Thickness:  return FormatDimension(row.Thickness);
                case ExportColumn.Quantity:   return row.Quantity.ToString(CultureInfo.InvariantCulture);
                case ExportColumn.Appearance: return row.AppearanceDisplay ?? string.Empty;
                default: throw new ArgumentOutOfRangeException(nameof(column), column, null);
            }
        }

        private static string FormatDimension(double mm)
        {
            return Math.Round(mm, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void BodiesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }

    /// <summary>
    /// Columns that appear in the Copy All clipboard payload and the Excel export. <see cref="Preview"/>
    /// is special: it has no text representation (clipboard skips it) and ExcelExporter renders it as
    /// an embedded PNG anchored to the cell. The other columns are kept narrow on purpose - the user
    /// originally asked for "tên chi tiết, dài, rộng, dày, số lượng, Appearance" so the output stays
    /// paste-friendly into their existing BOM workbook.
    /// </summary>
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum ExportColumn
    {
        Preview,
        BodyName,
        Length,
        Width,
        Thickness,
        Quantity,
        Appearance
    }

    /// <summary>
    /// One row in the "Active part" dropdown. Wraps a <see cref="ModelDoc2"/> with a
    /// human-readable label and a stable key (the file path) so the picker can preserve
    /// selection across <see cref="BodyExportWindow.RefreshOpenParts"/> rebuilds even when
    /// SolidWorks reorders its document list between calls.
    /// </summary>
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class OpenPartItem
    {
        /// <summary>
        /// Synthetic dropdown row that runs the file-open dialog instead of binding a part.
        /// </summary>
        public bool IsOpenPartCommand { get; set; }

        public ModelDoc2 Model { get; set; }
        public string DisplayName { get; set; }
        public string PathName { get; set; }
        public override string ToString() => DisplayName ?? string.Empty;
    }

    /// <summary>
    /// Visual style of a transient toast notification shown at the bottom of the window.
    /// </summary>
    internal enum ToastKind
    {
        Info,
        Success,
        Error
    }
}
