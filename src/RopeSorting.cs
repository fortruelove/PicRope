using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ScreenLine
{
    internal sealed partial class ShelfWindow
    {
        const string SortFormat = "PicRope.InternalCard";
        readonly string sortToken = Guid.NewGuid().ToString("N");
        readonly Border insertionMark = new Border { Width = 3, Height = 160, Background = Brushes.DodgerBlue, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        string sortingKey;
        internal event Action<string> SortingFailed;

        void InitializeSorting()
        {
            viewport.Background = Brushes.Transparent;
            viewport.AllowDrop = true;
            viewport.PreviewDragOver += SortOver;
            viewport.PreviewDrop += SortDrop;
            viewport.DragLeave += delegate { insertionMark.Visibility = Visibility.Collapsed; };
            content.Children.Add(insertionMark); Panel.SetZIndex(insertionMark, 1000);
        }

        string[] SortKeys()
        {
            return gallery.Hanging.Where(item => cards.ContainsKey(item.Path) && !cards[item.Path].Detached).Select(item => item.Path).ToArray();
        }

        internal bool OnSortingRope(Point point)
        {
            return Revealed && point.X >= 0 && point.X < viewport.ActualWidth && point.Y >= 0 && point.Y < Math.Min(220, viewport.ActualHeight);
        }

        int SortInsertion(Point point)
        {
            int count = SortKeys().Length;
            double left = (content.Width - count * 172) / 2;
            return Math.Max(0, Math.Min(count, (int)Math.Floor((point.X - left + 86) / 172)));
        }

        bool OwnSort(DragEventArgs e)
        {
            return sortingKey != null && Revealed && e.Data.GetDataPresent(SortFormat)
                && e.Data.GetData(SortFormat) as string == sortToken && SortKeys().Contains(sortingKey);
        }

        internal void BeginSort(ShotCard card, DataObject payload)
        {
            sortingKey = card.Item.Path; payload.SetData(SortFormat, sortToken);
            card.SetSortingHighlight(true);
            rope.Stroke = Brushes.DodgerBlue;
            rope.Effect = new DropShadowEffect { Color = Colors.DodgerBlue, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
            Preview.Dismiss();
        }

        internal void EndSort(ShotCard card)
        {
            sortingKey = null; card.SetSortingHighlight(false);
            rope.Effect = null; RopeLine.Apply(rope, Appearance);
            insertionMark.Visibility = Visibility.Collapsed;
        }

        internal void DragForSorting(ShotCard card, DataObject payload)
        {
            try { BeginSort(card, payload); DragDrop.DoDragDrop(card.Surface, payload, DragDropEffects.Copy | DragDropEffects.Move); }
            catch (Exception error) { SortError(error); }
            finally { EndSort(card); }
        }

        void SortError(Exception error)
        {
            gallery.Store.Log(error);
            if (SortingFailed != null) SortingFailed("排序未完成，请重试。" + error.Message);
        }

        void SortOver(object sender, DragEventArgs e)
        {
            e.Handled = true; e.Effects = DragDropEffects.None;
            Point point = e.GetPosition(viewport);
            if (!OwnSort(e) || !OnSortingRope(point)) { insertionMark.Visibility = Visibility.Collapsed; return; }
            e.Effects = DragDropEffects.Move;
            if (point.X < 32) scrollMotion.Target = Math.Max(0, scrollMotion.Target - 24);
            else if (point.X > viewport.ActualWidth - 32) scrollMotion.Target = Math.Min(ScrollLimit, scrollMotion.Target + 24);
            int index = SortInsertion(e.GetPosition(content));
            Canvas.SetLeft(insertionMark, (content.Width - SortKeys().Length * 172) / 2 + index * 172 - 4);
            Canvas.SetTop(insertionMark, 24); insertionMark.Visibility = Visibility.Visible;
            StartRendering();
        }

        void SortDrop(object sender, DragEventArgs e)
        {
            e.Handled = true; e.Effects = DragDropEffects.None;
            try
            {
                if (!OwnSort(e)) return;
                if (gallery.Reorder(sortingKey, SortInsertion(e.GetPosition(content)), SortKeys(), OnSortingRope(e.GetPosition(viewport))))
                { e.Effects = DragDropEffects.Move; LayoutCards(); StartRendering(); }
            }
            catch (Exception error) { SortError(error); }
            finally { insertionMark.Visibility = Visibility.Collapsed; }
        }
    }
}
