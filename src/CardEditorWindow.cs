using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ScreenLine {
// Changes stay in this dialog until Save; cancelling never changes the collection.
internal sealed class CardEditorWindow : Window {
    public readonly TextBox Body = new TextBox { AcceptsReturn=true, AcceptsTab=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Disabled, MinHeight=72, Padding=new Thickness(0, 6, 0, 6), FontSize=16, BorderThickness=new Thickness(0), Background=Brushes.Transparent };
    public CaptureItem[] SelectedPictures { get; private set; }
    public TextItem[] SelectedTexts { get; private set; }
    public readonly List<ClipboardPicture> NewPictures = new List<ClipboardPicture>();
    public readonly HashSet<string> RemovedPictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ClipboardPicture, BitmapSource> draftThumbnails = new Dictionary<ClipboardPicture, BitmapSource>();
    readonly TextBlock feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=LightTheme.Brush("#65717F"),Margin=new Thickness(0,8,0,8)};
    readonly CaptureItem original;
    readonly Action<string> openPicture;
    readonly bool readOnly;
    public CardEditorWindow(Gallery gallery, CaptureItem item, bool readOnly, Action<string> openPicture = null) {
        this.openPicture=openPicture; this.readOnly=readOnly;
        original = item;
        SelectedPictures = new CaptureItem[0];
        SelectedTexts = new TextItem[0];
        Title = readOnly ? "查看便签" : "编辑便签";
        Width = 900; Height = 700; MinWidth = 640; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Background = LightTheme.Brush("#FCFBF8"); Foreground = LightTheme.Brush("#242821");

        var root = new DockPanel { Background = Background };
        var surface = new Grid();
        surface.Children.Add(root);
        var shade = new Border { Background = LightTheme.Brush("#660E130D"), Visibility = Visibility.Collapsed };
        surface.Children.Add(shade);
        Content = surface;

        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome {
            CaptionHeight = 44, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0)
        });
        var header = new DockPanel { Height = 44, Margin = new Thickness(16, 0, 10, 0) };
        var close = LightTheme.Button("", Close, "quiet");
        close.Content = LightTheme.CloseIcon();
        close.Width = close.Height = 30;
        close.MinWidth = 0;
        close.Padding = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭便签");
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(LightTheme.BasketLogo(22));
        brand.Children.Add(new TextBlock { Text = "PicRope   " + Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(brand);
        var headerRule = new Border { Child = header, BorderBrush = LightTheme.Brush("#E5E2DB"), BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(headerRule, Dock.Top);
        root.Children.Add(headerRule);
        string[] picturePaths = gallery.PicturePaths(item);
        var footer = new DockPanel { Margin = new Thickness(22, 10, 22, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (!readOnly) actions.Children.Add(LightTheme.Button("取消", () => DialogResult = false, "quiet"));
        var save = LightTheme.Button(readOnly ? "关闭" : "保存", () => DialogResult = true);
        save.Background = LightTheme.Brush("#244D36");
        save.BorderBrush = save.Background;
        save.Foreground = Brushes.White;
        save.IsDefault = true;
        actions.Children.Add(save);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        feedback.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(feedback);
        var footerRule = new Border { Child = footer, BorderBrush = LightTheme.Brush("#E5E2DB"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(footerRule, Dock.Bottom);
        root.Children.Add(footerRule);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var panel = new StackPanel { MaxWidth = 730, Margin = new Thickness(34, 28, 34, 28) };
        scroll.Content = panel;
        root.Children.Add(scroll);
        string text = item.IsText ? item.Text.Content : gallery.Note(item);
        var title = new TextBlock { Text = LightTheme.MemoTitle(item, text), FontWeight = FontWeights.Bold, FontSize = 34, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(title);
        var created = item.IsText ? item.Text.CreatedUtc : gallery.Store.CardCreated(item.Path);
        var type = item.IsText ? "文字" : string.IsNullOrWhiteSpace(text) ? "图片" : "图文";
        panel.Children.Add(new TextBlock { Text = type + " · " + created.ToLocalTime().ToString("MM-dd HH:mm"),
            Foreground = LightTheme.Brush("#666960"), Margin = new Thickness(0, 6, 0, 16) });
        Body.Text = text;
        Body.IsReadOnly = readOnly;
        Body.TextChanged += delegate { title.Text = LightTheme.MemoTitle(item, Body.Text); };
        panel.Children.Add(Body);
        Body.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
            if (scroll.ScrollableHeight <= 0) return;
            var wheel = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = Mouse.MouseWheelEvent };
            scroll.RaiseEvent(wheel);
            e.Handled = wheel.Handled;
        };

        var addedText = new StackPanel();
        panel.Children.Add(addedText);
        var attachmentHeading = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 8) };
        if (!readOnly || picturePaths.Length > 0) panel.Children.Add(attachmentHeading);
        var picturePanel = new WrapPanel();
        var hover = new HoverPreview(this, gallery.Store.State.HoverPreview);
        bool closed = false;
        Closed += delegate { closed = true; hover.Dispose(); };
        Deactivated += delegate { hover.Dismiss(); };
        Activated += delegate { Topmost = true; };
        scroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e) {
            if (e.VerticalChange != 0) hover.Dismiss();
        };
        var existingThumbnails = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        bool canDetach = !readOnly && (picturePaths.Length > 1 ||
            (picturePaths.Length == 1 && !string.IsNullOrWhiteSpace(gallery.Note(item))));
        Action renderDraft = null;
        renderDraft = delegate {
            hover.Dismiss();
            addedText.Children.Clear();
            foreach (var card in SelectedPictures) {
                AddDraftText(addedText, gallery.Note(card), null);
            }
            foreach (var note in SelectedTexts) {
                AddDraftText(addedText, note.Content, delegate {
                    SelectedTexts = SelectedTexts.Where(selected => selected != note).ToArray();
                    renderDraft();
                });
            }

            picturePanel.Children.Clear();
            var originalPaths = picturePaths.Where(path => !RemovedPictures.Contains(path)).ToArray();
            var selectedPaths = SelectedPictures.SelectMany(gallery.PicturePaths).Where(path => !RemovedPictures.Contains(path))
                .Except(originalPaths, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int total = originalPaths.Length + selectedPaths.Length + NewPictures.Count;
            attachmentHeading.Text = "附件 · " + total + " 张";
            bool tiled = total > 1;
            foreach (string path in originalPaths) {
                Action remove = canDetach ? (Action)delegate {
                    RemovedPictures.Add(path);
                    renderDraft();
                    feedback.Text = "保存后，移出的原有图片会拆分到篮子";
                } : null;
                AddDraftPicture(picturePanel, LoadThumbnail(existingThumbnails, item, path), path, tiled, hover, remove);
            }
            foreach (string path in selectedPaths) {
                var source = SelectedPictures.First(card => gallery.PicturePaths(card).Contains(path, StringComparer.OrdinalIgnoreCase));
                AddDraftPicture(picturePanel, LoadThumbnail(existingThumbnails, source, path), path, tiled, hover, delegate {
                    SelectedPictures = SelectedPictures.Where(card => card != source).ToArray();
                    renderDraft();
                }, "取消加入这条篮子备忘");
            }
            foreach (var draft in NewPictures) {
                AddDraftPicture(picturePanel, draftThumbnails[draft], null, tiled, hover, delegate {
                    NewPictures.Remove(draft);
                    draftThumbnails.Remove(draft);
                    renderDraft();
                });
            }
        };

        if (!readOnly) {
            var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var file = LightTheme.Button("添加本地图片", delegate { ChooseFile(); renderDraft(); });
            var paste = LightTheme.Button("粘贴图片", null);
            paste.Click += async delegate {
                paste.IsEnabled = save.IsEnabled = false;
                try {
                    await PastePicture(() => closed);
                    if (!closed) renderDraft();
                }
                finally { paste.IsEnabled = save.IsEnabled = true; }
            };
            var basket = LightTheme.Button("从篮子选择…", delegate {
                var picker = new BasketPickerWindow(gallery, item, SelectedPictures, SelectedTexts) { Owner = this };
                shade.Visibility = Visibility.Visible;
                try { if (picker.ShowDialog() != true) return; }
                finally { shade.Visibility = Visibility.Collapsed; }
                SelectedPictures = picker.Pictures;
                SelectedTexts = picker.Texts;
                renderDraft();
                feedback.Text = "修改后请保存";
            });
            foreach (var button in new[] { file, paste, basket }) {
                button.Foreground = LightTheme.Brush("#244D36");
                button.Margin = new Thickness(0, 0, 8, 4);
                tools.Children.Add(button);
            }
            panel.Children.Add(tools);
            feedback.Text = "修改后请保存";
        }
        panel.Children.Add(picturePanel);
        renderDraft();
        if (!readOnly) PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
        };
    }

    static void AddDraftText(Panel panel, string text, Action remove)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var entry = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (remove != null) {
            var button = LightTheme.Button("移除", remove, "quiet");
            button.MinWidth = 0;
            button.Padding = new Thickness(8, 0, 8, 0);
            DockPanel.SetDock(button, Dock.Right);
            entry.Children.Add(button);
        }
        entry.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16 });
        panel.Children.Add(entry);
    }

    BitmapSource LoadThumbnail(Dictionary<string, BitmapSource> cache, CaptureItem card, string path)
    {
        BitmapSource bitmap;
        if (cache.TryGetValue(path, out bitmap)) return bitmap;
        try {
            bitmap = path == card.Path ? card.Image.Thumbnail : ImageContent.Read(path, false).Thumbnail;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
        catch (FormatException) { }
        cache[path] = bitmap;
        return bitmap;
    }

    void AddDraftPicture(Panel panel, BitmapSource bitmap, string path, bool tiled, HoverPreview hover,
        Action remove, string removeHint = "从当前便签移除图片")
    {
        var entry = new Grid { Width = tiled ? 208 : 380, Height = tiled ? 146 : 230,
            Margin = new Thickness(0, 0, 8, 8), Background = LightTheme.Brush("#F1EEE8") };
        if (bitmap == null) {
            entry.Children.Add(new TextBlock { Text = "无法预览图片", HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center });
        } else {
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            entry.Children.Add(image);
            if (path != null) {
                image.Cursor = Cursors.Hand;
                image.ToolTip = "双击查看原图";
                image.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                    if (e.ClickCount == 2) { e.Handled = true; OpenPicture(path); }
                };
                image.MouseEnter += delegate { hover.Enter(image, path); };
                image.MouseLeave += delegate { hover.Leave(image); };
                image.PreviewMouseDown += delegate { hover.Dismiss(); };
            }
        }
        if (remove != null) {
            var button = LightTheme.PictureRemoveButton(entry, remove);
            button.ToolTip = removeHint;
            entry.Children.Add(button);
        }
        panel.Children.Add(entry);
    }

    internal void OpenPicturePreview() {
        if(!readOnly || original.IsText)return;
        OpenPicture(original.Path);
    }
    void OpenPicture(string path) {
        try {
            if(!File.Exists(path))throw new FileNotFoundException("原图片已被移动或删除。");
            Topmost=false;
            if(openPicture!=null)openPicture(path);
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});
        } catch(Exception error){Topmost=true;feedback.Text="无法打开原图："+error.Message;}
    }
    void ChooseFile() {
        var dialog=new OpenFileDialog {Title="选择配图",Multiselect=true,Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"};
        if(dialog.ShowDialog(this)!=true)return;
        try {
            foreach (string path in dialog.FileNames) {
                var bytes = File.ReadAllBytes(path);
                var image = ImageContent.Decode(bytes);
                AddPicture(new ClipboardPicture { Bytes=bytes, Extension=Path.GetExtension(path) }, image.Thumbnail);
            }
            feedback.Text = "待保存的新图片：" + NewPictures.Count + " 张";
        }
        catch(Exception error){feedback.Text="无法读取图片："+error.Message;}
    }
    void AddPicture(ClipboardPicture picture, BitmapSource thumbnail)
    {
        NewPictures.Add(picture);
        draftThumbnails.Add(picture, thumbnail);
    }

    async Task PastePicture(Func<bool> isClosed)
    {
        try {
            var card = await CardClipboard.Read(WindowsNative.GetClipboardSequenceNumber(), true);
            if (isClosed()) return;
            if (card == null || card.Picture == null) { feedback.Text = "剪贴板中没有图片。"; return; }
            AddPicture(card.Picture, ImageContent.Decode(card.Picture.Bytes).Thumbnail);
            if (string.IsNullOrWhiteSpace(Body.Text) && !string.IsNullOrWhiteSpace(card.Text)) Body.Text = card.Text;
            feedback.Text = "已粘贴配图，保存后生效。";
        } catch (Exception error) { feedback.Text = "粘贴未完成：" + error.Message; }
    }
}
}
