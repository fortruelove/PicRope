using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenLine
{
    internal sealed partial class BasketWindow
    {
        readonly ShelfWindow popupShelf;
        Polygon popupPointer;

        void InitializePopup(Border card)
        {
            MinWidth = MinHeight = 0;
            Content = null;
            var root = new Grid();
            popupPointer = new Polygon { Points = new PointCollection { new Point(0, 12), new Point(12, 0), new Point(24, 12) }, Fill = Brushes.White,
                Width = 24, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            card.Margin = new Thickness(16, 11, 16, 16); card.CornerRadius = new CornerRadius(12);
            root.Children.Add(card); root.Children.Add(popupPointer); Content = root;
            popupShelf.SizeChanged += PopupShelfMoved; popupShelf.LocationChanged += PopupShelfMoved; popupShelf.RopeHidden += Close;
            Deactivated += delegate { if (IsVisible) Close(); };
            Closed += delegate
            {
                popupShelf.SizeChanged -= PopupShelfMoved; popupShelf.LocationChanged -= PopupShelfMoved; popupShelf.RopeHidden -= Close;
            };
        }

        void PopupShelfMoved(object sender, EventArgs e) { PositionPopup(); }

        internal void PositionPopup()
        {
            if (popupShelf == null || popupShelf.Display == null) return;
            var area = popupShelf.Display.WorkingArea;
            double dpi = WindowsNative.MonitorScale(popupShelf.Display);
            var anchor = popupShelf.BasketAnchor;
            Width = Math.Min(1040, area.Width / dpi - 32);
            Height = Math.Min(660, Math.Max(240, (area.Bottom - Math.Max(area.Top, anchor.Y)) / dpi - 16));
            double left = Math.Max(area.Left + 8 * dpi, Math.Min(area.Right - (Width + 8) * dpi, anchor.X - (Width - 76) * dpi));
            double top = Math.Max(area.Top + 4 * dpi, Math.Min(area.Bottom - Height * dpi, anchor.Y));
            popupPointer.Margin = new Thickness(Math.Max(30, Math.Min(Width - 54, (anchor.X - left) / dpi - 12)), 0, 0, 0);
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) WindowsNative.SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(Width * dpi), (int)Math.Round(Height * dpi), 0x10);
        }
    }
}
