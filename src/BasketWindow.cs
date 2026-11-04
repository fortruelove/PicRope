using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine
{
    internal static class PictureBasket
    {
        static readonly Dictionary<string, BitmapSource> pictures = new Dictionary<string, BitmapSource>();
        // 原图中的裁切边界与穿绳支点，保证换皮肤后挂钩仍贴住绳子。
        static readonly Int32Rect[] bounds = {
            new Int32Rect(40, 90, 1170, 1045), new Int32Rect(50, 105, 1150, 1000),
            new Int32Rect(110, 100, 1050, 975), new Int32Rect(20, 115, 1220, 1005),
            new Int32Rect(50, 95, 1160, 1020)
        };
        static readonly Point[] hooks = { new Point(592, 155), new Point(584, 165), new Point(565, 178), new Point(599, 175), new Point(573, 155) };
        static readonly double[] rims = { 735, 710, 740, 710, 700 };

        public static BitmapSource Source(LineTheme theme)
        {
            BitmapSource picture;
            if (pictures.TryGetValue(theme.Id, out picture)) return picture;
            picture = SkinImages.Crop("PicRope.Basket." + theme.Id, bounds[Array.IndexOf(LineTheme.All, theme)]);
            pictures.Add(theme.Id, picture);
            return picture;
        }

        public static Point Hook(LineTheme theme)
        {
            int index = Array.IndexOf(LineTheme.All, theme);
            return new Point((hooks[index].X - bounds[index].X) * 112 / bounds[index].Width,
                (hooks[index].Y - bounds[index].Y) * 112 / bounds[index].Width);
        }

        public static Point Mouth(LineTheme theme)
        {
            int index = Array.IndexOf(LineTheme.All, theme);
            return new Point(56, (rims[index] - bounds[index].Y) * 112 / bounds[index].Width);
        }
    }

    internal sealed partial class BasketWindow : Window
    {
        static readonly ControlTemplate choiceTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
          <Border x:Name='Surface' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' CornerRadius='8'><ContentPresenter HorizontalAlignment='Stretch'/></Border>
          <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F7F9FC'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        readonly Gallery gallery;
        readonly Action<bool> setAutoHang;
        readonly Action<CaptureItem> hang;
        readonly Action<ShotAction, CaptureItem> act;
        readonly List<CaptureItem> visibleMemos = new List<CaptureItem>();
        internal readonly Button CombineButton;
        internal readonly Button SettingsButton;
        bool basketMenuOpen;
        readonly WrapPanel previewTiles = new WrapPanel();
        readonly Button removePreviewPicture;
        readonly HoverPreview pictureHover;
        readonly ScrollViewer detailScroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        readonly Border pictureSection = new Border { Background = LightTheme.Brush("#EEF5FF"),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Visibility = Visibility.Collapsed };
        string[] previewPaths = new string[0];
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly StackPanel thumbnails = new StackPanel();
        readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly TextBlock empty = new TextBlock { TextAlignment = TextAlignment.Center, Foreground = LightTheme.Brush("#65717F"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock selectionStatus = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock status = new TextBlock { FontSize = 14, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center };
        readonly Image preview = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(18, 10, 18, 12) };
        readonly TextBlock previewHint = new TextBlock { Text = "选择备忘查看详情", FontSize = 14, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        readonly TextBlock pictureError = new TextBlock { Visibility = Visibility.Collapsed, FontSize = 14, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        readonly TextBlock documentTitle = new TextBlock { FontSize = 28, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock attachmentHeading = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        readonly TextBlock metadata = new TextBlock { FontSize = 12, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 4, 0, 12) };
        readonly Dictionary<string, Button> choices = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<CaptureItem, Image> pictures = new Dictionary<CaptureItem, Image>();
        readonly HashSet<Image> visiblePictures = new HashSet<Image>();
        readonly HashSet<Image> loadingPictures = new HashSet<Image>();
        static readonly SemaphoreSlim thumbnailSlots = new SemaphoreSlim(2, 2);
        static readonly SemaphoreSlim previewSlot = new SemaphoreSlim(1, 1);
        readonly TextBlock allCount = new TextBlock();
        readonly TextBlock pendingCount = new TextBlock();
        internal readonly Button PendingTab, AllTab, HangButton, DeleteButton, SelectAllButton;
        internal readonly CheckBox AutoHang;
        bool pendingOnly = false, refreshing, mounting, closed;
        internal readonly ArchiveChoice ArchiveOption;
        CaptureItem previewItem;
        ImageContent previewContent;
        int previewVersion;
        internal int SelectedCount { get { return selected.Count; } }
        internal Image Preview { get { return preview; } }
        internal int VisibleCount { get { return choices.Count; } }
        internal Button ChoiceFor(string path) { return choices[path]; }

        public BasketWindow(Gallery gallery, Action<bool> setAutoHang, Action<CaptureItem> hang, Action<ShotAction, CaptureItem> act, Func<Task<string>> paste = null, ShelfWindow shelf = null, TextCollection texts = null)
        {
            this.texts = texts ?? new TextCollection(gallery.Store);
            this.gallery = gallery; this.setAutoHang = setAutoHang; this.hang = hang; this.act = act;
            pictureHover = new HoverPreview(this, gallery.Store.State.HoverPreview);
            popupShelf = shelf;
            if (shelf != null) Owner = shelf;
            Title = "备忘篮子 · 图绳";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            AutomationProperties.SetAutomationId(this, "PictureBasket");
            var screen = WindowsNative.ScreenAtPointer();
            double dpi = WindowsNative.MonitorScale(screen);
            Width = Math.Min(1040, screen.WorkingArea.Width / dpi - 32);
            Height = Math.Min(660, screen.WorkingArea.Height / dpi - 48);
            MinWidth = Math.Min(720, Width); MinHeight = Math.Min(420, Height);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            var card = LightTheme.Card(body);
            card.Padding = new Thickness(0);
            card.Background = LightTheme.Brush("#FCFBF8");
            Content = card;
            if (shelf != null) InitializePopup(card);

            var sidebar = new DockPanel { Background = LightTheme.Brush("#F1EEE8"), Margin = new Thickness(1) };
            body.Children.Add(sidebar);
            var navigation = new StackPanel { Margin = new Thickness(12, 18, 12, 0) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            brand.Children.Add(LightTheme.BasketLogo(26));
            var brandText = new StackPanel();
            brandText.Children.Add(new TextBlock { Text = "备忘篮子", FontSize = 18, FontWeight = FontWeights.Bold });
            brandText.Children.Add(new TextBlock { Text = "PicRope", Foreground = LightTheme.Brush("#666960") });
            brand.Children.Add(brandText);
            navigation.Children.Add(brand);
            var switches = new StackPanel {Margin = new Thickness(14)};
            var imageSwitch = new StackPanel { Orientation = Orientation.Horizontal };
            imageSwitch.Children.Add(new TextBlock { Text="新图片自动上绳",FontSize=12,Foreground=LightTheme.Brush("#65717F"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0) });
            AutoHang = new CheckBox {Template=LightTheme.AutoHangSwitchTemplate,Cursor=Cursors.Hand};
            AutomationProperties.SetName(AutoHang,"新图片自动上绳"); AutoHang.Checked+=AutoChanged;AutoHang.Unchecked+=AutoChanged;
            imageSwitch.Children.Add(AutoHang);switches.Children.Add(imageSwitch);
            var textSwitch = new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
            textSwitch.Children.Add(new TextBlock {Text="新文字自动上绳",FontSize=12,Foreground=LightTheme.Brush("#65717F"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
            TextAutoHang = new CheckBox {Template=LightTheme.AutoHangSwitchTemplate,Cursor=Cursors.Hand}; AutomationProperties.SetName(TextAutoHang,"新文字自动上绳");
            TextAutoHang.Checked+=delegate {ChangeTextAutoHang();};TextAutoHang.Unchecked+=delegate {ChangeTextAutoHang();};
            textSwitch.Children.Add(TextAutoHang); switches.Children.Add(textSwitch);
            var globalArchive = new CheckBox { Content = "默认删除时归档", Margin = new Thickness(0, 10, 0, 0) };
            switches.Children.Add(globalArchive);
            SettingsButton = BasketAction("设置", null);
            SettingsButton.Content = LightTheme.IconLabel("\uE713", "设置");
            var settings = SettingsButton;
            settings.Margin = new Thickness(12);
            var settingsPopup = new Popup { PlacementTarget = settings, Placement = PlacementMode.Top, StaysOpen = false,
                AllowsTransparency = true, Child = LightTheme.Card(switches) };
            settingsPopup.Opened += delegate { basketMenuOpen = true; };
            settingsPopup.Closed += delegate { basketMenuOpen = false; };
            bool loadingSettings = false;
            settings.Click += delegate {
                loadingSettings = true;
                globalArchive.IsChecked = gallery.Store.State.ArchiveDeleted;
                loadingSettings = false;
                settingsPopup.IsOpen = !settingsPopup.IsOpen;
            };
            RoutedEventHandler saveArchive = delegate {
                if (loadingSettings) return;
                bool previous = gallery.Store.State.ArchiveDeleted;
                gallery.Store.State.ArchiveDeleted = globalArchive.IsChecked == true;
                try { gallery.Store.Save(); gallery.Store.NotifyArchiveSettings(); }
                catch (Exception error) {
                    gallery.Store.State.ArchiveDeleted = previous;
                    loadingSettings = true;
                    globalArchive.IsChecked = previous;
                    loadingSettings = false;
                    gallery.Store.Log(error);
                    status.Text = "归档设置保存失败，请重试";
                }
            };
            globalArchive.Checked += saveArchive;
            globalArchive.Unchecked += saveArchive;
            Closed += delegate { settingsPopup.IsOpen = false; };
            DockPanel.SetDock(settings, Dock.Bottom);
            sidebar.Children.Add(settings);
            sidebar.Children.Add(new ScrollViewer { Content = navigation, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

            NewMemoButton = LightTheme.Button("＋ 新建备忘", NewMemo);
            NewMemoButton.Background = LightTheme.Brush("#244D36");
            NewMemoButton.BorderBrush = NewMemoButton.Background;
            NewMemoButton.Foreground = Brushes.White;
            NewMemoButton.Margin = new Thickness(0, 0, 0, 12);
            navigation.Children.Add(NewMemoButton);
            AllTab=LightTheme.Button("全部",delegate {if(SaveMemo()){pendingOnly=false;memoTypeFilter=0;Refresh();}});
            PendingTab=LightTheme.Button("待挂",delegate {if(SaveMemo()){pendingOnly=!pendingOnly;Refresh();}});
            SetNavigationLabel(AllTab, "\uE8A9", "全部备忘", allCount);
            SetNavigationLabel(PendingTab, "\uE71B", "待挂上绳", pendingCount);
            navigation.Children.Add(AllTab); navigation.Children.Add(PendingTab);
            string[] typeNames={"图片","文字","图文"};
            string[] typeIcons = { "\uEB9F", "\uE8D2", "\uE91B" };
            for (int n = 0; n < typeNames.Length; n++) {
                int type = n + 1;
                var button = LightTheme.Button(typeNames[n], () => ChooseType(type));
                SetNavigationLabel(button, typeIcons[n], typeNames[n]);
                TypeButtons[n] = button;
                navigation.Children.Add(button);
            }
            foreach (var button in navigation.Children.OfType<Button>().Skip(1)) {
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Padding = new Thickness(8, 0, 8, 0);
                button.Margin = new Thickness(0, 0, 0, 4);
            }
            var inbox = new DockPanel { Background = LightTheme.Brush("#F9F8F5"), ClipToBounds = true };
            Grid.SetColumn(inbox, 1);
            body.Children.Add(inbox);
            Search=new TextBox {MinWidth=90,Padding=new Thickness(10,5,10,5),BorderThickness=new Thickness(0),Background=Brushes.Transparent,VerticalContentAlignment=VerticalAlignment.Center};
            var searchPanel=new Grid {HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(12,0,0,0)};
            searchPanel.Children.Add(new Border {CornerRadius=new CornerRadius(7),BorderBrush=LightTheme.Brush("#DCE2EA"),BorderThickness=new Thickness(1),Child=Search});
            var placeholder=new TextBlock {Text="搜索备忘",Foreground=LightTheme.Brush("#8390A1"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(11,0,0,0),IsHitTestVisible=false};searchPanel.Children.Add(placeholder);
            AutomationProperties.SetName(Search,"搜索备忘");Search.ToolTip="搜索文字、备注或图片文件名";
            Search.TextChanged+=delegate {placeholder.Visibility=Search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(!refreshing)Refresh();};
            searchPanel.Margin = new Thickness(12, 14, 12, 10);
            searchPanel.Height = 34;
            DockPanel.SetDock(searchPanel, Dock.Top);
            inbox.Children.Add(searchPanel);
            var selectionBar = new DockPanel { Margin = new Thickness(8, 6, 8, 10) };
            SelectAllButton = LightTheme.Button("全选", SelectAll, "quiet");
            DeleteButton = LightTheme.Button("删除所选", DeleteSelected, "quiet");
            SelectAllButton.MinWidth = DeleteButton.MinWidth = 0;
            SelectAllButton.Padding = DeleteButton.Padding = new Thickness(5, 0, 5, 0);
            DeleteButton.Foreground = LightTheme.Brush("#993F37");
            DockPanel.SetDock(DeleteButton, Dock.Right);
            selectionBar.Children.Add(DeleteButton);
            DockPanel.SetDock(SelectAllButton, Dock.Left);
            selectionBar.Children.Add(SelectAllButton);
            selectionBar.Children.Add(selectionStatus);
            DockPanel.SetDock(selectionBar, Dock.Bottom);
            inbox.Children.Add(selectionBar);
            var list = new Grid();
            scroll.Content = thumbnails;
            list.Children.Add(scroll);
            list.Children.Add(empty);
            inbox.Children.Add(list);
            if (paste != null) {
                PreviewKeyDown += async delegate(object sender, KeyEventArgs e) {
                    if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !MemoEditor.IsKeyboardFocusWithin && !Search.IsKeyboardFocusWithin) {
                        e.Handled = true;
                        if (SaveMemo()) status.Text = await paste();
                    }
                };
            }
            scroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e) {
                if (e.VerticalChange != 0 || e.HorizontalChange != 0) pictureHover.Dismiss();
                UpdateThumbnails();
            };
            Loaded += delegate { UpdateThumbnails(); if (previewItem != null) ShowPreview(previewItem); };
            var previewBody = new Grid { ClipToBounds = true };
            previewBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            previewBody.RowDefinitions.Add(new RowDefinition());
            previewBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var previewHeading = new DockPanel { Margin = new Thickness(12, 0, 10, 0) };
            var close = LightTheme.Button("", Close, "quiet");
            close.Content = LightTheme.CloseIcon();
            close.Width = close.Height = 28;
            close.MinWidth = 0;
            close.Padding = new Thickness(0);
            AutomationProperties.SetName(close, "关闭备忘篮子");
            DockPanel.SetDock(close, Dock.Right);
            previewHeading.Children.Add(close);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            HangButton = BasketAction("挂上绳子", Mount);
            CombineButton = BasketAction("组合", CombineSelection);
            HangButton.Content = LightTheme.IconLabel("\uE71B", "挂上绳子");
            actions.Children.Add(HangButton);
            CombineButton.Content = LightTheme.IconLabel("\uE8A9", "组合");
            actions.Children.Add(CombineButton);
            if (paste != null) {
                var pasteButton = BasketAction("粘贴", async delegate { if (SaveMemo()) status.Text = await paste(); });
                pasteButton.Content = LightTheme.IconLabel("\uE77F", "粘贴");
                actions.Children.Add(pasteButton);
            }
            previewHeading.Children.Add(actions);
            ArchiveOption = new ArchiveChoice(gallery.Store, message => status.Text = message);
            previewBody.Children.Add(new Border { Child = previewHeading, BorderBrush = LightTheme.Brush("#E5E2DB"), BorderThickness = new Thickness(0, 0, 0, 1) });
            Closed += delegate { ArchiveOption.Dispose(); };
            var detailContent = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
            detailScroll.Content = detailContent;
            detailScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e) {
                if (e.VerticalChange != 0) pictureHover.Dismiss();
            };
            Grid.SetRow(detailScroll, 1);
            previewBody.Children.Add(detailScroll);

            detailContent.Children.Add(documentTitle);
            detailContent.Children.Add(metadata);
            InitializeMemoEditor(detailContent);
            var imageContent = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            imageContent.Children.Add(attachmentHeading);
            var singlePicture = new Grid { Height = 158, HorizontalAlignment = HorizontalAlignment.Center };
            singlePicture.Children.Add(preview);
            singlePicture.Children.Add(pictureError);
            removePreviewPicture = LightTheme.PictureRemoveButton(singlePicture, delegate {
                if (previewPaths.Length == 1) DetachPreviewPicture(previewPaths[0]);
            });
            removePreviewPicture.Margin = new Thickness(18, 10, 0, 0);
            removePreviewPicture.Visibility = Visibility.Collapsed;
            singlePicture.Children.Add(removePreviewPicture);
            imageContent.Children.Add(singlePicture);
            imageContent.Children.Add(previewTiles);
            pictureSection.Child = imageContent;
            detailContent.Children.Add(pictureSection);
            Grid.SetRow(previewHint, 1);
            previewBody.Children.Add(previewHint);
            preview.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.ClickCount == 2 && previewItem != null && !previewItem.IsText) { e.Handled = true; OpenOriginal(); }
            };
            AttachPictureHover(preview, () => previewPaths.Length == 1 ? previewPaths[0] : null);
            preview.ToolTip = "双击打开原图";
            var detailsFooter = new DockPanel { Margin = new Thickness(16, 4, 12, 8) };
            var more = LightTheme.Button("···", null, "quiet");
            more.MinWidth = 34;
            AutomationProperties.SetName(more, "当前备忘更多操作");
            var moreMenu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Top };
            moreMenu.Opened += delegate { basketMenuOpen = true; };
            moreMenu.Closed += delegate { basketMenuOpen = false; };
            moreMenu.Items.Add(new MenuItem { Header = ArchiveOption, StaysOpenOnClick = true, Focusable = false });
            var copy = new MenuItem { Header = "复制内容" };
            copy.Click += delegate {
                if (previewItem != null && SaveMemo() && previewItem != null)
                    act(previewItem.IsText ? ShotAction.CopyText : string.IsNullOrWhiteSpace(MemoText(previewItem)) ? ShotAction.Copy : ShotAction.CopyBoth, previewItem);
            };
            moreMenu.Items.Add(copy);
            var delete = new MenuItem { Header = "删除备忘" };
            delete.Click += delegate { if (SaveMemo() && previewItem != null) act(ShotAction.Trash, previewItem); };
            moreMenu.Items.Add(delete);
            more.Click += delegate {
                copy.IsEnabled = delete.IsEnabled = previewItem != null;
                moreMenu.IsOpen = true;
            };
            DockPanel.SetDock(more, Dock.Right);
            detailsFooter.Children.Add(more);
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.FontSize = 12;
            status.Foreground = LightTheme.Brush("#42654D");
            detailsFooter.Children.Add(status);
            Grid.SetRow(detailsFooter, 2);
            previewBody.Children.Add(detailsFooter);
            var right = new Border { BorderBrush = LightTheme.Brush("#E5E2DB"), BorderThickness = new Thickness(1, 0, 0, 0), Child = previewBody };
            Grid.SetColumn(right, 2);
            body.Children.Add(right);
            SourceInitialized += delegate
            {
                if (popupShelf != null) { PositionPopup(); return; }
                int width = (int)Math.Round(Width * dpi), height = (int)Math.Round(Height * dpi);
                WindowsNative.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                    screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2,
                    screen.WorkingArea.Top + Math.Min((int)(190 * dpi), Math.Max(16, (screen.WorkingArea.Height - height) / 2)), width, height, 0x14);
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            gallery.Changed += GalleryChanged; this.texts.Changed+=GalleryChanged;
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!SaveMemo())e.Cancel=true;};
            Closed += delegate { closed = true; memoTimer.Stop(); this.texts.Changed-=GalleryChanged; gallery.Changed -= GalleryChanged; previewVersion++; preview.Source = null; foreach (var image in pictures.Values) image.Source = null; pictures.Clear(); visiblePictures.Clear(); };
            Closed += delegate { pictureHover.Dispose(); };
            Refresh();
        }

        void AutoChanged(object sender, RoutedEventArgs e)
        {
            if (!refreshing && gallery.Store.State.AutoHang != (AutoHang.IsChecked == true)) {try {setAutoHang(AutoHang.IsChecked==true);}catch(Exception error){gallery.Store.Log(error);refreshing=true;AutoHang.IsChecked=gallery.Store.State.AutoHang;refreshing=false;status.Text="自动上绳设置保存失败";}}
        }

        void GalleryChanged() { if (!mounting && !savingMemo) Refresh(); }

        void Refresh()
        {
            if(savingMemo || !SaveMemo())return;
            refreshing=true;AutoHang.IsChecked=gallery.Store.State.AutoHang;TextAutoHang.IsChecked=texts.AutoHang;refreshing=false;
            var all=AllMemos();
            selected.RemoveWhere(path=>!all.Any(i=>i.Path==path));
            pendingCount.Text = all.Count(item => !Mounted(item)).ToString();
            allCount.Text = all.Count.ToString();
            SetNavigationState(PendingTab, pendingOnly);
            SetNavigationState(AllTab, !pendingOnly && memoTypeFilter == 0);
            for (int n = 0; n < 3; n++) SetNavigationState(TypeButtons[n], memoTypeFilter == n + 1);
            double offset=scroll.VerticalOffset;thumbnails.Children.Clear();choices.Clear();pictures.Clear();visiblePictures.Clear();
            var items=all.Where(i=>(!pendingOnly || !Mounted(i)) && Matches(i)).OrderByDescending(Created).ToArray();
            selected.IntersectWith(items.Select(i=>i.Path));
            visibleMemos.Clear();
            pictureHover.Dismiss();
            visibleMemos.AddRange(items);
            foreach (var day in items.GroupBy(item => Created(item).ToLocalTime().Date)) {
                string label = day.Key == DateTime.Today ? "今天" : day.Key == DateTime.Today.AddDays(-1) ? "昨天" : day.Key.ToString("yyyy-MM-dd");
                thumbnails.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(14, 12, 0, 6) });
                foreach (var item in day) AddChoice(item);
            }
            Dispatcher.BeginInvoke(new Action(UpdateThumbnails), System.Windows.Threading.DispatcherPriority.Loaded);
            empty.Text=all.Count==0?"篮子还是空的\n新建备忘或粘贴内容开始记录":"没有匹配的备忘";
            empty.Visibility=items.Length==0?Visibility.Visible:Visibility.Collapsed;scroll.Visibility=items.Length==0?Visibility.Collapsed:Visibility.Visible;
            scroll.ScrollToVerticalOffset(offset);UpdateSelection();
            if(!creatingMemo) {
                var next=items.FirstOrDefault(i=>previewItem!=null && i.Path==previewItem.Path) ?? items.FirstOrDefault();
                if (next != previewItem || (next != null && !next.IsText &&
                    (previewContent != next.Image || !previewPaths.SequenceEqual(gallery.PicturePaths(next))))) ShowPreview(next);
                else {
                    ArchiveOption.SetKey(next==null?null:next.Path);
                    if (next != null && !memoDirty && MemoEditor.Text != MemoText(next)) LoadMemo(next);
                    UpdatePictureRemoval();
                }
            }
        }

        static Button BasketAction(string label, Action action)
        {
            var button = LightTheme.Button(label, action);
            AutomationProperties.SetName(button, label);
            button.Background = LightTheme.Brush("#EAF0E8");
            button.BorderBrush = LightTheme.Brush("#C5D2C3");
            button.Foreground = LightTheme.Brush("#244D36");
            button.Height = 30;
            button.MinWidth = 0;
            button.Padding = new Thickness(11, 0, 11, 0);
            button.Margin = new Thickness(0, 0, 6, 0);
            return button;
        }

        static void SetNavigationLabel(Button button, string icon, string label, TextBlock count = null)
        {
            var row = new DockPanel();
            if (count != null) {
                count.Margin = new Thickness(4, 0, 0, 0);
                count.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
            }
            row.Children.Add(LightTheme.IconLabel(icon, label));
            button.Content = row;
            AutomationProperties.SetName(button, label);
        }

        static void SetNavigationState(Button button, bool active)
        {
            button.Background = active ? LightTheme.Brush("#E1E8DD") : Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = active ? LightTheme.Brush("#244D36") : LightTheme.Brush("#363B34");
        }

        void UpdateRowAppearance(string path, Border frame)
        {
            bool current = previewItem != null && previewItem.Path == path;
            frame.BorderBrush = current ? LightTheme.Brush("#244D36") : Brushes.Transparent;
            frame.Background = current || selected.Contains(path) ? LightTheme.Brush("#E3ECE1") : Brushes.Transparent;
        }

        void AddChoice(CaptureItem item)
        {
            bool mounted=Mounted(item);string note=MemoText(item);
            var row = new DockPanel { Margin = new Thickness(8, 5, 8, 5) };
            var tile = new Grid { Width = 46, Height = 44, Background = LightTheme.Brush("#EEECE5"), Margin = new Thickness(0, 0, 8, 0) };
            if (item.IsText) tile.Children.Add(new TextBlock { Text = "文字", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = LightTheme.Brush("#666960") });
            else {
                var image = new Image { Stretch = Stretch.UniformToFill };
                tile.ClipToBounds = true;
                pictures[item] = image;
                tile.Children.Add(image);
                AttachPictureHover(tile, () => item.Path);
            }
            DockPanel.SetDock(tile, Dock.Left);
            row.Children.Add(tile);
            var mark = new TextBlock { Text = "✓", Foreground = LightTheme.Brush("#244D36"), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(mark, Dock.Right);
            row.Children.Add(mark);
            var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var headline = new DockPanel();
            var date = Created(item).ToLocalTime();
            var time = new TextBlock { Text = date.ToString("HH:mm"), FontSize = 11,
                Foreground = LightTheme.Brush("#666960"), Margin = new Thickness(5, 0, 0, 0) };
            DockPanel.SetDock(time, Dock.Right);
            headline.Children.Add(time);
            headline.Children.Add(new TextBlock { Text = LightTheme.MemoTitle(item, note),
                TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold });
            description.Children.Add(headline);
            string excerpt = (note ?? "").Trim();
            int lineBreak = excerpt.IndexOfAny(new[] { '\r', '\n' });
            if (lineBreak >= 0) excerpt = excerpt.Substring(lineBreak).Trim();
            if (excerpt.Length == 0) excerpt = MemoType(item);
            description.Children.Add(new TextBlock { Text = excerpt.Replace('\r', ' ').Replace('\n', ' '),
                TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Foreground = LightTheme.Brush("#666960"),
                Margin = new Thickness(0, 3, 0, 0) });
            row.Children.Add(description);
            var frame = new Border { Child = row, BorderThickness = new Thickness(3, 0, 0, 0) };
            var button = new Button { Content = frame, Template = choiceTemplate, Height = 58, Margin = new Thickness(4, 0, 4, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Action sync = delegate {
                bool chosen = selected.Contains(item.Path);
                mark.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
                frame.BorderBrush = chosen ? LightTheme.Brush("#244D36") : Brushes.Transparent;
                frame.Background = chosen ? LightTheme.Brush("#E3ECE1") : Brushes.Transparent;
                AutomationProperties.SetItemStatus(button, chosen ? (mounted ? "已选择，已挂上" : "已选择") : mounted ? "已挂上" : "待挂");
            };
            button.Click+=delegate {if(!SaveMemo())return;if(!selected.Add(item.Path))selected.Remove(item.Path);ShowPreview(item);sync();UpdateSelection();};
            var menu=new ImageMenu(delegate(ShotAction action){if(SaveMemo())act(action,item);});menu.SetCard(item.IsText,!string.IsNullOrEmpty(note),gallery.PicturePaths(item).Length>1);menu.ChooseColor=delegate(int color){try{gallery.Store.SetCardColor(item.Path,color);gallery.Notify();}catch(Exception error){gallery.Store.Log(error);status.Text="颜色保存失败，请重试";}};button.ContextMenu=menu;
            button.ToolTip=MemoType(item)+" · "+(item.IsText?note:Path.GetFileName(item.Path));AutomationProperties.SetName(button,MemoType(item)+" "+(item.IsText?note:Path.GetFileName(item.Path)));sync();thumbnails.Children.Add(button);choices[item.Path]=button;
        }

        void UpdateThumbnails()
        {
            if (closed) return;
            var viewport = new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight);
            visiblePictures.Clear();
            foreach (var entry in pictures)
            {
                var picture = entry.Value;
                var frame = (FrameworkElement)picture.Parent;
                if (frame.ActualWidth == 0 || !frame.IsDescendantOf(scroll)) continue;
                var bounds = frame.TransformToAncestor(scroll).TransformBounds(new Rect(frame.RenderSize));
                if (!bounds.IntersectsWith(viewport)) { picture.Source = null; continue; }
                visiblePictures.Add(picture);
                if (picture.Source != null) continue;
                var cached = entry.Key.Image.CachedThumbnail;
                if (cached != null) { picture.Source = cached; continue; }
                if (loadingPictures.Add(picture)) LoadThumbnail(entry.Key, picture);
            }
        }

        bool ThumbnailCurrent(CaptureItem item, Image picture, ImageContent content)
        {
            Image current;
            return !closed && visiblePictures.Contains(picture) && item.Image == content && pictures.TryGetValue(item, out current) && current == picture;
        }

        async void LoadThumbnail(CaptureItem item, Image picture)
        {
            ImageContent content = item.Image;
            try
            {
                await thumbnailSlots.WaitAsync();
                try
                {
                    if (!ThumbnailCurrent(item, picture, content)) return;
                    var bitmap = await Task.Run(() => content.Thumbnail);
                    if (ThumbnailCurrent(item, picture, content)) picture.Source = bitmap;
                }
                finally { thumbnailSlots.Release(); }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
            catch (FormatException) { }
            finally { loadingPictures.Remove(picture); }
        }

        internal static string TimeLabel(DateTime time)
        {
            if (time.Kind == DateTimeKind.Utc) time = time.ToLocalTime();
            return (time.Date == DateTime.Today ? "今天 " : time.ToString("MM-dd ")) + time.ToString("HH:mm");
        }

        async void ShowPreview(CaptureItem item, bool saveCurrent = true)
        {
            if(saveCurrent && previewItem!=item && !SaveMemo())return;
            pictureHover.Dismiss();
            int version=++previewVersion;bool changed=previewItem!=item;
            var previousItem = previewItem;
            previewItem=item;
            Button row;
            if (previousItem != null && choices.TryGetValue(previousItem.Path, out row)) UpdateRowAppearance(previousItem.Path, (Border)row.Content);
            if (item != null && choices.TryGetValue(item.Path, out row)) UpdateRowAppearance(item.Path, (Border)row.Content);
            HangButton.IsEnabled = selected.Count == 0 ? item != null && !Mounted(item) : visibleMemos.Any(card => selected.Contains(card.Path) && !Mounted(card));
            CombineButton.IsEnabled = selected.Count > 0 || item != null;
            previewPaths = item == null ? new string[0] : gallery.PicturePaths(item);
            bool tiled = previewPaths.Length > 1;
            UpdatePictureRemoval();
            previewTiles.Children.Clear();
            pictureSection.Visibility = previewPaths.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            previewTiles.Visibility = tiled ? Visibility.Visible : Visibility.Collapsed;
            ((Grid)preview.Parent).Visibility = previewPaths.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (changed) { detailScroll.ScrollToTop(); status.Text = ""; }
            previewContent=item==null?null:item.Image;
            ArchiveOption.SetKey(item==null?null:item.Path);
            if(changed || !MemoEditor.IsEnabled)LoadMemo(item);
            preview.Visibility=previewPaths.Length==1?Visibility.Visible:Visibility.Collapsed;
            preview.Source=previewPaths.Length==1?item.Image.CachedThumbnail:null;
            previewHint.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
            pictureError.Visibility = Visibility.Collapsed;
            documentTitle.Text = item == null ? "" : LightTheme.MemoTitle(item, MemoText(item));
            attachmentHeading.Text = "附件 · " + previewPaths.Length + " 张";
            metadata.Text=item==null?"":MemoType(item)+" · "+TimeLabel(Created(item));
            if (tiled) {
                ShowPreviewTiles(previewPaths, version);
                return;
            }
            if(item==null || item.IsText || !IsLoaded)return;
            try
            {
                await previewSlot.WaitAsync();
                try
                {
                    if (closed || version != previewVersion) return;
                    ImageContent content = item.Image;
                    var visual = PresentationSource.FromVisual(this);
                    double scale = visual == null ? 1 : visual.CompositionTarget.TransformToDevice.M11;
                    var panel = (Grid)preview.Parent;
                    double width = Math.Max(1, (pictureSection.ActualWidth - pictureSection.Padding.Left - pictureSection.Padding.Right - preview.Margin.Left - preview.Margin.Right) * scale);
                    double height = Math.Max(1, (panel.ActualHeight - preview.Margin.Top - preview.Margin.Bottom) * scale);
                    var picture = await Task.Run(() => ReadPreview(item.Path, content, width, height));
                    if (version == previewVersion) { preview.Source = picture; }
                }
                finally { previewSlot.Release(); }
            }
            catch (IOException) { if (version == previewVersion) { preview.Source = null; pictureError.Text = "图片已移动或删除"; pictureError.Visibility = Visibility.Visible; } }
            catch (UnauthorizedAccessException) { if (version == previewVersion) { preview.Source = null; pictureError.Text = "暂时无法读取这张图片"; pictureError.Visibility = Visibility.Visible; } }
            catch (NotSupportedException) { if (version == previewVersion) { preview.Source = null; pictureError.Text = "暂时无法预览这张图片"; pictureError.Visibility = Visibility.Visible; } }
            catch (FormatException) { if (version == previewVersion) { preview.Source = null; pictureError.Text = "暂时无法预览这张图片"; pictureError.Visibility = Visibility.Visible; } }
        }

        async void ShowPreviewTiles(string[] paths, int version)
        {
            var thumbnails = new List<Image>();
            foreach (string path in paths) {
                var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };
                var tile = new Border { Width = 142, Height = 104, Margin = new Thickness(4), CornerRadius = new CornerRadius(5),
                    Background = Brushes.White, Child = image, Cursor = Cursors.Hand, ToolTip = "双击打开原图" };
                AutomationProperties.SetName(tile, System.IO.Path.GetFileName(path));
                AttachPictureHover(tile, () => path);
                tile.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                    if (e.ClickCount == 2) { e.Handled = true; OpenPreviewPicture(path); }
                };
                var entry = new Grid();
                entry.Children.Add(tile);
                entry.Children.Add(LightTheme.PictureRemoveButton(entry, () => DetachPreviewPicture(path)));
                previewTiles.Children.Add(entry);
                thumbnails.Add(image);
            }
            for (int i = 0; i < paths.Length; i++) {
                await thumbnailSlots.WaitAsync();
                try {
                    if (closed || version != previewVersion) return;
                    string path = paths[i];
                    var bitmap = await Task.Run(() => ImageContent.Read(path, false).Thumbnail);
                    if (closed || version != previewVersion) return;
                    thumbnails[i].Source = bitmap;
                } catch (Exception error) {
                    if (closed || version != previewVersion) return;
                    gallery.Store.Log(error);
                    var tile = (Border)thumbnails[i].Parent;
                    tile.Child = new TextBlock { Text = "图片无法预览", HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center };
                } finally { thumbnailSlots.Release(); }
            }
        }

        void UpdatePictureRemoval()
        {
            removePreviewPicture.Visibility = previewItem != null && previewPaths.Length == 1 &&
                !string.IsNullOrWhiteSpace(gallery.Note(previewItem)) ? Visibility.Visible : Visibility.Collapsed;
        }

        void DetachPreviewPicture(string path)
        {
            if (!SaveMemo() || previewItem == null || !previewPaths.Contains(path)) return;
            try {
                CaptureItem remaining;
                mounting = true;
                try { remaining = gallery.Combine(new[] { previewItem }, removedPictures: new[] { path }); }
                finally { mounting = false; }
                selected.Clear();
                Refresh();
                if (remaining != null && visibleMemos.Contains(remaining)) ShowPreview(remaining);
                status.Text = "图片已拆分到篮子";
            } catch (Exception error) {
                gallery.Store.Log(error);
                status.Text = "移除未完成：" + error.Message;
            }
        }

        void AttachPictureHover(FrameworkElement tile, Func<string> pathForTile)
        {
            tile.MouseEnter += delegate {
                pictureHover.SetEnabled(gallery.Store.State.HoverPreview);
                string path = pathForTile();
                if (path != null) pictureHover.Enter(tile, path);
            };
            tile.MouseLeave += delegate { pictureHover.Leave(tile); };
            tile.PreviewMouseDown += delegate { pictureHover.Dismiss(); };
        }

        static BitmapSource ReadPreview(string path, ImageContent content, double width, double height)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream;
                double ratio = Math.Min(1, Math.Min(width / content.PixelWidth, height / content.PixelHeight));
                if (width / content.PixelWidth < height / content.PixelHeight) bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(content.PixelWidth * ratio));
                else bitmap.DecodePixelHeight = Math.Max(1, (int)Math.Ceiling(content.PixelHeight * ratio));
                bitmap.EndInit();
                var color = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int stride = color.PixelWidth * 4; var pixels = new byte[stride * color.PixelHeight];
                color.CopyPixels(pixels, stride, 0);
                var result = BitmapSource.Create(color.PixelWidth, color.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                result.Freeze(); return result;
            }
        }

        void SelectAll()
        {
            bool clear = choices.Count > 0 && choices.Keys.All(selected.Contains);
            foreach (string path in choices.Keys)
                if (clear) selected.Remove(path); else selected.Add(path);
            Refresh();
        }

        void UpdateSelection()
        {
            SelectAllButton.IsEnabled = choices.Count > 0;
            SelectAllButton.Content = choices.Count > 0 && choices.Keys.All(selected.Contains) ? "取消全选" : "全选";
            CombineButton.IsEnabled = selected.Count > 0 || previewItem != null;
            int pending=AllMemos().Count(i=>selected.Contains(i.Path)&&!Mounted(i));
            selectionStatus.Text = selected.Count > 0 ? "已选 " + selected.Count + " 条" : "";
            HangButton.IsEnabled = pending > 0 || (selected.Count == 0 && previewItem != null && !Mounted(previewItem));
            DeleteButton.IsEnabled = selected.Count > 0;
        }

        void Mount()
        {
            if(!SaveMemo())return;
            var items=AllMemos().Where(i=>selected.Contains(i.Path)&&!Mounted(i)).ToArray();
            if (items.Length == 0 && selected.Count == 0 && previewItem != null && !Mounted(previewItem)) items = new[] { previewItem };
            mounting = true;
            try { foreach(var item in items) {if(item.IsText)texts.SetHanging(new[]{item.Text},true);else hang(item);} }
            finally { mounting = false; selected.Clear(); Refresh(); }
            status.Text = "已挂上 " + items.Count(Mounted) + " 条";
        }

        void DeleteSelected()
        {
            if(!SaveMemo())return;
            var items=AllMemos().Where(item=>selected.Contains(item.Path)).ToArray();
            if (items.Length == 0) return;
            mounting = true;
            try { foreach (var item in items) act(ShotAction.Trash, item); }
            finally { mounting = false; Refresh(); }
            int removed=items.Count(item=>item.IsText?!texts.Items.Contains(item.Text):!gallery.Collected.Contains(item));
            status.Text = "已删除 " + removed + " 条" + (removed < items.Length ? " · " + (items.Length - removed) + " 条未删除" : "");
        }
    }
}
