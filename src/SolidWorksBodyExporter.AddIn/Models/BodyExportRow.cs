using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SolidWorksBodyExporter.AddIn.Models
{
    // Every type below is referenced by XAML by literal name (DataTrigger Value="New", binding
    // paths, etc.) and by Newtonsoft.Json deserialisation (which looks up property names via
    // reflection). Renaming any of them would silently break the UI or the saved-metadata
    // round-trip, so they are pinned for any obfuscator that respects [Obfuscation].

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum DimensionAxis
    {
        X,
        Y,
        Z
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum BodyRowStatus
    {
        Unchanged,
        New,
        Deleted,
        SizeChanged
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum DimensionSlot
    {
        Length,
        Width,
        Thickness
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class DimensionMapping
    {
        public DimensionAxis LengthAxis { get; set; }
        public DimensionAxis WidthAxis { get; set; }
        public DimensionAxis ThicknessAxis { get; set; }
    }

    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class BodyExportRow : INotifyPropertyChanged
    {
        private string _displayName;
        private DimensionAxis _lengthAxis;
        private DimensionAxis _widthAxis;
        private DimensionAxis _thicknessAxis;
        private bool _isEditing;
        private ImageSource _thumbnail;

        public string PluginBodyId { get; set; }

        public string SolidWorksBodyName { get; set; }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value)
                {
                    return;
                }

                _displayName = value;
                OnPropertyChanged();
            }
        }

        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }

        public double Length => GetAxisValue(LengthAxis);

        public double Width => GetAxisValue(WidthAxis);

        public double Thickness => GetAxisValue(ThicknessAxis);

        public DimensionAxis LengthAxis
        {
            get => _lengthAxis;
            set => SetAxisWithSwap(DimensionSlot.Length, value);
        }

        public DimensionAxis WidthAxis
        {
            get => _widthAxis;
            set => SetAxisWithSwap(DimensionSlot.Width, value);
        }

        public DimensionAxis ThicknessAxis
        {
            get => _thicknessAxis;
            set => SetAxisWithSwap(DimensionSlot.Thickness, value);
        }

        /// <summary>
        /// Assigns <paramref name="newAxis"/> to the requested dimension slot while preserving the
        /// invariant that {<see cref="LengthAxis"/>, <see cref="WidthAxis"/>,
        /// <see cref="ThicknessAxis"/>} is a permutation of {X, Y, Z}. If another slot currently
        /// owns <paramref name="newAxis"/>, the two slots swap their axis assignments so no axis
        /// is duplicated and no slot is left unassigned. This is the primitive the ComboBox-based
        /// axis editor relies on in the UI.
        /// </summary>
        private void SetAxisWithSwap(DimensionSlot slot, DimensionAxis newAxis)
        {
            var current = GetAxis(slot);
            if (current == newAxis)
            {
                return;
            }

            // Find which other slot currently owns the requested axis. We always expect to find
            // exactly one such slot because the triple is a permutation. If we don't (defensive),
            // we still set the requested slot and emit changes so the UI reflects the value.
            DimensionSlot? otherSlot = null;
            foreach (DimensionSlot candidate in Enum.GetValues(typeof(DimensionSlot)))
            {
                if (candidate == slot)
                {
                    continue;
                }
                if (GetAxis(candidate) == newAxis)
                {
                    otherSlot = candidate;
                    break;
                }
            }

            AssignAxisField(slot, newAxis);

            if (otherSlot.HasValue)
            {
                AssignAxisField(otherSlot.Value, current);
                NotifyAxisChanged(otherSlot.Value);
            }

            NotifyAxisChanged(slot);
        }

        private void AssignAxisField(DimensionSlot slot, DimensionAxis axis)
        {
            switch (slot)
            {
                case DimensionSlot.Length:
                    _lengthAxis = axis;
                    break;
                case DimensionSlot.Width:
                    _widthAxis = axis;
                    break;
                case DimensionSlot.Thickness:
                    _thicknessAxis = axis;
                    break;
            }
        }

        private void NotifyAxisChanged(DimensionSlot slot)
        {
            switch (slot)
            {
                case DimensionSlot.Length:
                    OnPropertyChanged(nameof(LengthAxis));
                    OnPropertyChanged(nameof(Length));
                    break;
                case DimensionSlot.Width:
                    OnPropertyChanged(nameof(WidthAxis));
                    OnPropertyChanged(nameof(Width));
                    break;
                case DimensionSlot.Thickness:
                    OnPropertyChanged(nameof(ThicknessAxis));
                    OnPropertyChanged(nameof(Thickness));
                    break;
            }
        }

        public string MaterialName { get; set; }

        public string MaterialDisplay => string.IsNullOrWhiteSpace(MaterialName) ? "Default" : MaterialName;

        public string ColorName { get; set; }

        public string TextureName { get; set; }

        public string ColorHex { get; set; }

        public string AppearanceDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(TextureName) && !string.IsNullOrWhiteSpace(ColorName))
                {
                    return TextureName + " (" + ColorName + ")";
                }
                if (!string.IsNullOrWhiteSpace(TextureName))
                {
                    return TextureName;
                }
                if (!string.IsNullOrWhiteSpace(ColorName))
                {
                    return ColorName;
                }
                return "Default";
            }
        }

        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Names of every SolidWorks body that this row represents after identical-body grouping.
        /// For a single, unique body this contains exactly <see cref="SolidWorksBodyName"/>. For a
        /// pattern/array/mirror of N identical bodies the row stores all N body names so save and
        /// metadata round-tripping can still find every underlying body if the user later edits
        /// the display name shared by the group.
        /// </summary>
        public IReadOnlyList<string> GroupMemberBodyNames { get; set; } = Array.Empty<string>();

        public BodyRowStatus Status { get; set; }

        public bool IsDeleted => Status == BodyRowStatus.Deleted;

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value)
                {
                    return;
                }

                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public ImageSource Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (ReferenceEquals(_thumbnail, value))
                {
                    return;
                }

                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Swaps the axis assignment between two of the Length/Width/Thickness slots so that the
        /// triple always remains a permutation of {X, Y, Z}. This is the primitive used by the
        /// drag-and-drop edit mode in the UI.
        /// </summary>
        public void SwapAxes(DimensionSlot a, DimensionSlot b)
        {
            if (a == b)
            {
                return;
            }

            var axisA = GetAxis(a);
            var axisB = GetAxis(b);
            SetAxis(a, axisB);
            SetAxis(b, axisA);
        }

        public DimensionAxis GetAxis(DimensionSlot slot)
        {
            switch (slot)
            {
                case DimensionSlot.Length:
                    return LengthAxis;
                case DimensionSlot.Width:
                    return WidthAxis;
                case DimensionSlot.Thickness:
                    return ThicknessAxis;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        private void SetAxis(DimensionSlot slot, DimensionAxis axis)
        {
            switch (slot)
            {
                case DimensionSlot.Length:
                    LengthAxis = axis;
                    break;
                case DimensionSlot.Width:
                    WidthAxis = axis;
                    break;
                case DimensionSlot.Thickness:
                    ThicknessAxis = axis;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        public DimensionMapping GetMapping()
        {
            return new DimensionMapping
            {
                LengthAxis = LengthAxis,
                WidthAxis = WidthAxis,
                ThicknessAxis = ThicknessAxis
            };
        }

        private double GetAxisValue(DimensionAxis axis)
        {
            switch (axis)
            {
                case DimensionAxis.X:
                    return X;
                case DimensionAxis.Y:
                    return Y;
                case DimensionAxis.Z:
                    return Z;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
