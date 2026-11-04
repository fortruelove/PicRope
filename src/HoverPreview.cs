using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenLine
{
    internal sealed class HoverPreview : IDisposable
    {
        sealed class Request
        {
            public ShotCard Card;
            public FrameworkElement Tile;
            public string Path;
            public ImageContent Image;
            public Rect Anchor, Area;
            public double Scale;
            public int Revision;
        }

        readonly Window owner;
        readonly ShelfWindow shelf;
        readonly DispatcherTimer delay = new DispatcherTimer();
        readonly DispatcherTimer leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        Request target;
        Request pending;
        int revision;
        bool enabled, loading, disposed;
        DateTime lastShown = DateTime.MinValue;
        PreviewWindow window;
        internal bool Visible { get { return window != null && window.IsVisible && window.Opacity > 0; } }
        internal CaptureItem CurrentItem { get; private set; }
        internal BitmapSource Picture { get { return window == null ? null : window.Picture; } }
        internal PreviewWindow Window { get { return window; } }

        public HoverPreview(Window owner, bool enabled)
        {
            this.owner = owner; shelf = owner as ShelfWindow; this.enabled = enabled;
            delay.Tick += delegate { delay.Stop(); Queue(); };
            leave.Tick += delegate { leave.Stop(); target = null; Hide(); };
        }

        public void Enter(ShotCard card)
        {
            if (card.Item.IsText) return;
            Enter(new Request { Card = card, Path = card.Item.Path, Image = card.Item.Image });
        }

        public void Enter(FrameworkElement tile, string path)
        {
            Enter(new Request { Tile = tile, Path = path });
        }

        void Enter(Request request)
        {
            if (!CanPreview(request)) return;
            bool quick = Visible || (DateTime.UtcNow - lastShown).TotalMilliseconds < 1000;
            if (target != null && target.Card == request.Card && target.Tile == request.Tile && (Visible || delay.IsEnabled)) {
                leave.Stop();
                return;
            }
            delay.Stop(); leave.Stop(); revision++; pending = null;
            target = request; Hide();
            delay.Interval = TimeSpan.FromMilliseconds(quick ? 150 : 400);
            delay.Start();
        }

        public void Leave(ShotCard card)
        {
            if (target != null && target.Card == card) Leave();
        }

        public void Leave(FrameworkElement tile)
        {
            if (target != null && target.Tile == tile) Leave();
        }

        void Leave()
        {
            delay.Stop(); revision++; pending = null;
            leave.Stop(); leave.Start();
        }

        public void Dismiss()
        {
            delay.Stop(); leave.Stop(); revision++; pending = null; target = null; Hide();
        }

        public void SetEnabled(bool value) { enabled = value; if (!value) Dismiss(); }

        void Hide()
        {
            CurrentItem = null;
            if (window != null) { window.Clear(); window.Close(); window = null; }
        }

        void Queue()
        {
            if (!CanPreview(target)) return;
            Rect anchor;
            if (target.Card != null) anchor = target.Card.PhotoRect();
            else {
                var tile = target.Tile;
                anchor = new Rect(tile.PointToScreen(new Point()), tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight)));
            }
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)(anchor.Left + anchor.Width / 2), (int)anchor.Bottom));
            var area = screen.WorkingArea;
            target.Anchor = anchor;
            target.Area = new Rect(area.Left, area.Top, area.Width, area.Height);
            target.Scale = WindowsNative.MonitorScale(screen);
            target.Revision = revision;
            pending = target;
            if (!loading) Load();
        }

        async void Load()
        {
            loading = true;
            while (pending != null && !disposed)
            {
                Request request = pending; pending = null;
                BitmapSource image = null;
                try { image = await Task.Run(() => Read(request)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (NotSupportedException) { }
                catch (InvalidOperationException) { }
                catch (FormatException) { }
                if (!IsCurrent(request)) continue;
                if (window == null) window = new PreviewWindow(owner);
                PreviewWindow prepared = window;
                bool ready = await prepared.Prepare(image, request.Image, request.Anchor, request.Area, request.Scale);
                if (!ready || !IsCurrent(request) || window != prepared || !prepared.IsVisible) continue;
                CurrentItem = request.Card == null ? null : request.Card.Item; lastShown = DateTime.UtcNow; prepared.Present();
            }
            loading = false;
        }

        bool IsCurrent(Request request)
        {
            return request.Revision == revision && target == request && CanPreview(request);
        }

        bool CanPreview(Request request)
        {
            if (disposed || !enabled || request == null) return false;
            if (request.Card == null)
                return request.Tile.IsVisible && request.Tile.IsMouseOver && PresentationSource.FromVisual(request.Tile) != null;
            var card = request.Card;
            return shelf.Revealed && !card.IsDisposed && !card.Busy && !card.Detached &&
                !card.Flying && !card.Releasing && request.Image == card.Item.Image;
        }

        static BitmapSource Read(Request request)
        {
            if (request.Image == null) request.Image = ImageContent.Read(request.Path, false);
            Size size = PreviewSize(request.Image.PixelWidth, request.Image.PixelHeight, request.Anchor, request.Area, request.Scale);
            using (var stream = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream; image.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(size.Width * request.Scale));
                image.EndInit(); image.Freeze(); return image;
            }
        }

        internal static Size PreviewSize(int width, int height, Rect anchor, Rect area, double scale)
        {
            double maxWidth = Math.Max(1, Math.Min(640, area.Width / scale - 72));
            double maxHeight = Math.Max(1, Math.Min(480, area.Height / scale - 72));
            double ratio = Math.Min(1, Math.Min(maxWidth * scale / width, maxHeight * scale / height));
            double below = (area.Bottom - anchor.Bottom) / scale - 20;
            double above = (anchor.Top - area.Top) / scale - 20;
            if (height * ratio / scale + 56 > Math.Max(below, above) && below >= above && below >= 256)
                ratio = Math.Min(ratio, (below - 56) * scale / height);
            return new Size(Math.Max(1, width * ratio / scale), Math.Max(1, height * ratio / scale));
        }

        internal static Rect Place(Rect anchor, Rect area, double width, double height, double scale)
        {
            double margin = 8 * scale, gap = 12 * scale;
            width = Math.Min(width * scale, Math.Max(1, area.Width - 2 * margin));
            height = Math.Min(height * scale, Math.Max(1, area.Height - 2 * margin));
            double x = Math.Max(area.Left + margin, Math.Min(area.Right - margin - width, anchor.Left + (anchor.Width - width) / 2));
            double y = anchor.Bottom + gap;
            if (y + height > area.Bottom - margin) y = anchor.Top - gap - height;
            y = Math.Max(area.Top + margin, Math.Min(area.Bottom - margin - height, y));
            return new Rect(x, y, width, height);
        }

        public void Dispose()
        {
            if (disposed) return;
            Dismiss(); disposed = true;
        }
    }

    internal sealed class PreviewWindow : Window
    {
        readonly Image image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(28),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 32, ShadowDepth = 10, Direction = 270, Opacity = 0.8 } };
        readonly Border error = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8),
            Child = new TextBlock { Text = "图片暂时无法预览", Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(22, 18, 22, 18) } };
        readonly Grid body = new Grid();
        internal BitmapSource Picture { get { return image.Source as BitmapSource; } }

        public PreviewWindow(Window owner)
        {
            Owner = owner; Title = "图片预览 · 图绳";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
            body.Children.Add(image); body.Children.Add(error);
            Content = body;
            SourceInitialized += delegate
            {
                var handle = new WindowInteropHelper(this).Handle;
                WindowsNative.PrepareOverlay(handle); WindowsNative.PassMouse(handle, true);
            };
        }

        public async Task<bool> Prepare(BitmapSource picture, ImageContent source, Rect anchor, Rect area, double scale)
        {
            Opacity = 0;
            image.Source = picture; error.Visibility = picture == null ? Visibility.Visible : Visibility.Collapsed;
            image.Visibility = picture == null ? Visibility.Collapsed : Visibility.Visible;
            if (picture != null)
            {
                Size size = HoverPreview.PreviewSize(source.PixelWidth, source.PixelHeight, anchor, area, scale);
                image.Width = size.Width; image.Height = size.Height;
                Width = image.Width + 56; Height = image.Height + 56;
            }
            else { Width = 200; Height = 72; }
            Rect bounds = HoverPreview.Place(anchor, area, Width, Height, scale);
            var handle = new WindowInteropHelper(this).EnsureHandle();
            WindowsNative.SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x10);
            if (!IsVisible) Show();
            UpdateLayout();
            return await NextFrame() && await NextFrame();
        }

        static async Task<bool> NextFrame()
        {
            var completion = new TaskCompletionSource<bool>();
            EventHandler handler = delegate { completion.TrySetResult(true); };
            CompositionTarget.Rendering += handler;
            try { return await Task.WhenAny(completion.Task, Task.Delay(500)) == completion.Task; }
            finally { CompositionTarget.Rendering -= handler; }
        }

        public void Present() { Opacity = 1; }
        public void Clear() { image.Source = null; }
    }
}
