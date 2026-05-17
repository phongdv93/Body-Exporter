using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Ui
{
    /// <summary>
    /// Three-cell editor for the Length / Width / Thickness axis assignment of a single body.
    /// In edit mode the user can grab any cell and drop it on another to swap their axis assignments.
    /// A ghost popup follows the cursor while dragging, and on drop both cells slide from the swapped
    /// position back to their resting position so the swap reads as a physical motion.
    /// </summary>
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public partial class DimensionTriplet : UserControl
    {
        private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(180);

        private Point _dragOrigin;
        private DimensionSlot? _pressedSlot;
        private Border _activeSource;
        private Popup _ghostPopup;

        public DimensionTriplet()
        {
            InitializeComponent();
            foreach (var box in EnumerateBoxes())
            {
                box.RenderTransformOrigin = new Point(0.5, 0.5);
                box.RenderTransform = new TranslateTransform();
                box.PreviewMouseLeftButtonDown += Box_PreviewMouseLeftButtonDown;
            }
        }

        private BodyExportRow Row => DataContext as BodyExportRow;

        private IEnumerable<Border> EnumerateBoxes()
        {
            yield return LengthBox;
            yield return WidthBox;
            yield return ThicknessBox;
        }

        private static DimensionSlot ReadSlot(Border box)
        {
            return (DimensionSlot)Enum.Parse(typeof(DimensionSlot), (string)box.Tag);
        }

        private Border BoxForSlot(DimensionSlot slot)
        {
            switch (slot)
            {
                case DimensionSlot.Length: return LengthBox;
                case DimensionSlot.Width: return WidthBox;
                case DimensionSlot.Thickness: return ThicknessBox;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private void Box_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = Row;
            if (row == null || !row.IsEditing)
            {
                return;
            }

            _activeSource = (Border)sender;
            _pressedSlot = ReadSlot(_activeSource);
            _dragOrigin = e.GetPosition(this);

            CaptureMouse();
            PreviewMouseMove += Self_PreviewMouseMove;
            PreviewMouseLeftButtonUp += Self_PreviewMouseLeftButtonUp;
            LostMouseCapture += Self_LostMouseCapture;
            e.Handled = true;
        }

        private void Self_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedSlot == null || _activeSource == null)
            {
                return;
            }

            var current = e.GetPosition(this);
            var delta = current - _dragOrigin;

            if (_ghostPopup == null &&
                (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance))
            {
                StartGhost(_activeSource);
            }

            if (_ghostPopup != null)
            {
                _activeSource.Opacity = 0.35;
                MoveGhost(e.GetPosition(this));
                UpdateHoverHighlight(e.GetPosition(this));
            }
        }

        private void Self_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            FinishDrag(e.GetPosition(this));
        }

        private void Self_LostMouseCapture(object sender, MouseEventArgs e)
        {
            FinishDrag(null);
        }

        private void FinishDrag(Point? releasePoint)
        {
            try
            {
                if (_activeSource == null)
                {
                    return;
                }

                Border target = null;
                if (releasePoint.HasValue && _ghostPopup != null)
                {
                    target = HitTestSlot(releasePoint.Value);
                }

                CloseGhost();
                ClearHoverHighlight();
                _activeSource.Opacity = 1.0;

                if (target != null && target != _activeSource && _pressedSlot.HasValue && Row != null)
                {
                    var sourceSlot = _pressedSlot.Value;
                    var targetSlot = ReadSlot(target);
                    SwapWithSlide(sourceSlot, targetSlot);
                }
            }
            finally
            {
                PreviewMouseMove -= Self_PreviewMouseMove;
                PreviewMouseLeftButtonUp -= Self_PreviewMouseLeftButtonUp;
                LostMouseCapture -= Self_LostMouseCapture;
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                }
                _activeSource = null;
                _pressedSlot = null;
            }
        }

        private void StartGhost(Border source)
        {
            var brush = new VisualBrush(source) { Stretch = Stretch.None };
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = source.ActualWidth,
                Height = source.ActualHeight,
                Fill = brush,
                Opacity = 0.85,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Opacity = 0.35
                }
            };

            _ghostPopup = new Popup
            {
                Child = rect,
                AllowsTransparency = true,
                PlacementTarget = this,
                Placement = PlacementMode.Relative,
                IsHitTestVisible = false,
                StaysOpen = true,
                IsOpen = true
            };
        }

        private void MoveGhost(Point pointInControl)
        {
            if (_ghostPopup == null)
            {
                return;
            }

            var sourcePos = _activeSource.TranslatePoint(new Point(0, 0), this);
            _ghostPopup.HorizontalOffset = pointInControl.X - (_dragOrigin.X - sourcePos.X);
            _ghostPopup.VerticalOffset = pointInControl.Y - (_dragOrigin.Y - sourcePos.Y);
        }

        private void CloseGhost()
        {
            if (_ghostPopup != null)
            {
                _ghostPopup.IsOpen = false;
                _ghostPopup = null;
            }
        }

        private Border HitTestSlot(Point pointInControl)
        {
            foreach (var box in EnumerateBoxes())
            {
                var topLeft = box.TranslatePoint(new Point(0, 0), this);
                var rect = new Rect(topLeft, new Size(box.ActualWidth, box.ActualHeight));
                if (rect.Contains(pointInControl))
                {
                    return box;
                }
            }
            return null;
        }

        private void UpdateHoverHighlight(Point pointInControl)
        {
            var hovered = HitTestSlot(pointInControl);
            foreach (var box in EnumerateBoxes())
            {
                if (box == _activeSource)
                {
                    continue;
                }

                box.BorderBrush = box == hovered
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x40))
                    : EditingBorderBrush();
            }
        }

        private void ClearHoverHighlight()
        {
            foreach (var box in EnumerateBoxes())
            {
                box.ClearValue(Border.BorderBrushProperty);
            }
        }

        private static Brush EditingBorderBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x3D, 0x7B, 0xCE));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Swap the axis assignment and slide both affected boxes from each other's positions back
        /// to their resting position. The boxes themselves never move in the layout; we only animate
        /// their TranslateTransform so the text content appears to slide into place.
        /// </summary>
        private void SwapWithSlide(DimensionSlot sourceSlot, DimensionSlot targetSlot)
        {
            var sourceBox = BoxForSlot(sourceSlot);
            var targetBox = BoxForSlot(targetSlot);

            var sourcePos = sourceBox.TranslatePoint(new Point(0, 0), this);
            var targetPos = targetBox.TranslatePoint(new Point(0, 0), this);
            var deltaX = sourcePos.X - targetPos.X;

            Row.SwapAxes(sourceSlot, targetSlot);

            AnimateTranslateX(sourceBox, -deltaX, 0);
            AnimateTranslateX(targetBox, deltaX, 0);
        }

        private static void AnimateTranslateX(Border target, double from, double to)
        {
            if (!(target.RenderTransform is TranslateTransform transform))
            {
                transform = new TranslateTransform();
                target.RenderTransform = transform;
            }

            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(SlideDuration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }
    }
}
