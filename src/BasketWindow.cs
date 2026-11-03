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
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly UniformGrid thumbnails = new UniformGrid { Columns = 3, VerticalAlignment = VerticalAlignment.Top };
        readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly TextBlock empty = new TextBlock { TextAlignment = TextAlignment.Center, Foreground = LightTheme.Brush("#65717F"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock status = new TextBlock { FontSize = 14, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center };
        readonly Image preview = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(18, 10, 18, 12) };
        readonly TextBlock previewHint = new TextBlock { Text = "选择图片，查看大图", FontSize = 14, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock metadata = new TextBlock { FontSize = 12, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(16, 0, 16, 14) };
        readonly Dictionary<string, Button> choices = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<CaptureItem, Image> pictures = new Dictionary<CaptureItem, Image>();
        readonly HashSet<Image> visiblePictures = new HashSet<Image>();
        readonly HashSet<Image> loadingPictures = new HashSet<Image>();
        static readonly SemaphoreSlim thumbnailSlots = new SemaphoreSlim(2, 2);
        static readonly SemaphoreSlim previewSlot = new SemaphoreSlim(1, 1);
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
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 48 });
            body.RowDefinitions.Add(new RowDefinition());
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
            var card = LightTheme.Card(body); card.Padding = new Thickness(0); Content = card;
            if (shelf != null) InitializePopup(card);

            var heading = new Grid { Margin = new Thickness(22, 0, 18, 0) };
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "备忘篮子", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center });
            heading.Children.Add(title);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var switches = new StackPanel {Orientation=Orientation.Horizontal};
            var imageSwitch = new StackPanel { Orientation = Orientation.Horizontal };
            imageSwitch.Children.Add(new TextBlock { Text="新图片自动上绳",FontSize=12,Foreground=LightTheme.Brush("#65717F"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0) });
            AutoHang = new CheckBox {Template=LightTheme.AutoHangSwitchTemplate,Cursor=Cursors.Hand};
            AutomationProperties.SetName(AutoHang,"新图片自动上绳"); AutoHang.Checked+=AutoChanged;AutoHang.Unchecked+=AutoChanged;
            imageSwitch.Children.Add(AutoHang);switches.Children.Add(imageSwitch);
            var textSwitch = new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(20,0,0,0)};
            textSwitch.Children.Add(new TextBlock {Text="新文字自动上绳",FontSize=12,Foreground=LightTheme.Brush("#65717F"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
            TextAutoHang = new CheckBox {Template=LightTheme.AutoHangSwitchTemplate,Cursor=Cursors.Hand}; AutomationProperties.SetName(TextAutoHang,"新文字自动上绳");
            TextAutoHang.Checked+=delegate {ChangeTextAutoHang();};TextAutoHang.Unchecked+=delegate {ChangeTextAutoHang();};
            textSwitch.Children.Add(TextAutoHang);switches.Children.Add(textSwitch);tools.Children.Add(switches);
            var close = LightTheme.Button("", Close, "quiet"); close.Content = LightTheme.CloseIcon();
            close.Width = close.Height = 28; close.MinWidth = 0; close.Padding = new Thickness(0); close.Margin = new Thickness(22, 0, 0, 0);
            AutomationProperties.SetName(close, "关闭备忘篮子"); tools.Children.Add(close); Grid.SetColumn(tools, 1); heading.Children.Add(tools);
            heading.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (popupShelf == null && e.OriginalSource is TextBlock) DragMove(); };
            body.Children.Add(new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = heading });

            var toolbar = new Grid {Margin=new Thickness(22,6,22,8)};
            toolbar.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});toolbar.ColumnDefinitions.Add(new ColumnDefinition());
            var tabs = new StackPanel {Orientation=Orientation.Horizontal};
            NewMemoButton=LightTheme.Button("新建备忘",NewMemo);tabs.Children.Add(NewMemoButton);
            if(paste!=null) {
                tabs.Children.Add(LightTheme.Button("粘贴",async delegate {if(SaveMemo())status.Text=await paste();}));
                PreviewKeyDown+=async delegate(object sender,KeyEventArgs e){if(e.Key==Key.V && Keyboard.Modifiers==ModifierKeys.Control && !MemoEditor.IsKeyboardFocusWithin && !Search.IsKeyboardFocusWithin){e.Handled=true;if(SaveMemo())status.Text=await paste();}};
            }
            HangButton=LightTheme.Button("挂上绳子",Mount,"primary");tabs.Children.Add(HangButton);
            tabs.Children.Add(new Border {Width=1,Height=22,Background=LightTheme.Brush("#DCE2EA"),Margin=new Thickness(6,0,10,0)});
            AllTab=LightTheme.Button("全部",delegate {if(SaveMemo()){pendingOnly=false;memoTypeFilter=0;Refresh();}});
            PendingTab=LightTheme.Button("待挂",delegate {if(SaveMemo()){pendingOnly=!pendingOnly;Refresh();}});
            tabs.Children.Add(AllTab);tabs.Children.Add(PendingTab);
            string[] typeNames={"图片","文字","图文"};
            for(int n=0;n<3;n++){int type=n+1;var button=LightTheme.Button(typeNames[n],delegate {ChooseType(type);});TypeButtons[n]=button;tabs.Children.Add(button);}
            toolbar.Children.Add(tabs);
            Search=new TextBox {MinWidth=90,Padding=new Thickness(10,5,10,5),BorderThickness=new Thickness(0),Background=Brushes.Transparent,VerticalContentAlignment=VerticalAlignment.Center};
            var searchPanel=new Grid {HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(12,0,0,0)};
            searchPanel.Children.Add(new Border {CornerRadius=new CornerRadius(7),BorderBrush=LightTheme.Brush("#DCE2EA"),BorderThickness=new Thickness(1),Child=Search});
            var placeholder=new TextBlock {Text="搜索备忘",Foreground=LightTheme.Brush("#8390A1"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(11,0,0,0),IsHitTestVisible=false};searchPanel.Children.Add(placeholder);
            AutomationProperties.SetName(Search,"搜索备忘");Search.ToolTip="搜索文字、备注或图片文件名";
            Search.TextChanged+=delegate {placeholder.Visibility=Search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(!refreshing)Refresh();};
            Grid.SetColumn(searchPanel,1);toolbar.Children.Add(searchPanel);
            Action sizeActions=delegate {bool compact=toolbar.ActualWidth>0&&toolbar.ActualWidth<800;foreach(var button in tabs.Children.OfType<Button>()){button.MinWidth=0;button.FontSize=compact?12:13;button.Padding=new Thickness(compact?6:12,0,compact?6:12,0);button.Margin=new Thickness(0,0,compact?4:6,0);}};
            sizeActions();toolbar.SizeChanged+=delegate {sizeActions();};
            var tabBar=new Border {BorderBrush=LightTheme.Brush("#E8EBEF"),BorderThickness=new Thickness(0,0,0,1),Child=toolbar};Grid.SetRow(tabBar,1);body.Children.Add(tabBar);

            var content = new Grid { ClipToBounds = true };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            var left = new Grid { Margin = new Thickness(16, 10, 12, 12), ClipToBounds = true };
            scroll.Content = thumbnails; left.Children.Add(scroll); left.Children.Add(empty); content.Children.Add(left);
            scroll.ScrollChanged += delegate { UpdateThumbnails(); };
            Loaded += delegate { UpdateThumbnails(); if (previewItem != null) ShowPreview(previewItem); };
            var previewBody = new Grid { ClipToBounds = true };
            previewBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            previewBody.RowDefinitions.Add(new RowDefinition());
            previewBody.RowDefinitions.Add(new RowDefinition());
            previewBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var previewHeading = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            previewHeading.Children.Add(new TextBlock { Text = "详情", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = LightTheme.Brush("#65717F") });
            var editMemo=LightTheme.Button("配图 / 编辑…",delegate {if(SaveMemo()&&previewItem!=null)act(ShotAction.CardEdit,previewItem);});
            editMemo.Height=28;editMemo.FontSize=12;editMemo.HorizontalAlignment=HorizontalAlignment.Right;editMemo.Margin=new Thickness(0,0,142,0);previewHeading.Children.Add(editMemo);
            ArchiveOption = new ArchiveChoice(gallery.Store, delegate(string message) { status.Text = message; });
            ArchiveOption.HorizontalAlignment = HorizontalAlignment.Right; previewHeading.Children.Add(ArchiveOption); previewBody.Children.Add(previewHeading);
            Closed += delegate { ArchiveOption.Dispose(); };
            Grid.SetRow(preview, 1); previewBody.Children.Add(preview);
            Grid.SetRow(previewHint, 1); previewBody.Children.Add(previewHint);
            InitializeMemoEditor(previewBody);
            Grid.SetRow(metadata, 3); previewBody.Children.Add(metadata);
            preview.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if(e.ClickCount==2 && previewItem!=null && !previewItem.IsText){e.Handled=true;OpenOriginal();} };
            previewBody.ToolTip = "双击打开原图";
            var previewPanel = new Border { Background = LightTheme.Brush("#F1F4F8"), CornerRadius = new CornerRadius(9), Margin = new Thickness(12, 10, 20, 12), Child = previewBody };
            var right = new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(1, 0, 0, 0), Child = previewPanel };
            Grid.SetColumn(right, 1); content.Children.Add(right);
            Grid.SetRow(content, 2); body.Children.Add(content);

            var footer = new Grid { Margin = new Thickness(24, 0, 24, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SelectAllButton = LightTheme.Button("全选", SelectAll);
            SelectAllButton.Background = LightTheme.Brush("#E4EEFC");
            SelectAllButton.BorderBrush = LightTheme.Brush("#8DB0DF");
            SelectAllButton.Foreground = LightTheme.Brush("#205CA7");
            SelectAllButton.MinWidth = 112; SelectAllButton.Height = 38;
            SelectAllButton.VerticalAlignment = VerticalAlignment.Center;
            SelectAllButton.Margin = new Thickness(0, 0, 16, 0); footer.Children.Add(SelectAllButton);
            status.TextTrimming = TextTrimming.CharacterEllipsis; status.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(status, 1); footer.Children.Add(status);
            DeleteButton = LightTheme.Button("删除所选", DeleteSelected);
            DeleteButton.Width = 112; DeleteButton.Height = 38;
            DeleteButton.HorizontalAlignment = HorizontalAlignment.Right; DeleteButton.VerticalAlignment = VerticalAlignment.Center;
            DeleteButton.Margin = new Thickness(0, 0, 12, 0); DeleteButton.Foreground = LightTheme.Brush("#B74E4A");
            DeleteButton.Background = LightTheme.Brush("#FFF8F7"); DeleteButton.BorderBrush = LightTheme.Brush("#ECD6D2");
            DeleteButton.ToolTip = "按每条备忘的归档设置删除";
            AutomationProperties.SetName(DeleteButton, "删除所选图片"); Grid.SetColumn(DeleteButton, 2); footer.Children.Add(DeleteButton);
            foreach (var button in new[] { PendingTab, AllTab, SelectAllButton, DeleteButton, HangButton })
            {
                button.FontWeight = FontWeights.Normal;
            }
            foreach (var button in new[] { SelectAllButton, DeleteButton }) button.FontSize = 14;
            var bottom = new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
            Grid.SetRow(bottom, 3); body.Children.Add(bottom);
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
            int pending=all.Count(i=>!Mounted(i));PendingTab.Content="待挂 "+pending;AllTab.Content="全部 "+all.Count;
            PendingTab.Tag=pendingOnly?"primary":null;AllTab.Tag=!pendingOnly&&memoTypeFilter==0?"primary":null;
            for(int n=0;n<3;n++)TypeButtons[n].Tag=memoTypeFilter==n+1?"primary":null;
            double offset=scroll.VerticalOffset;thumbnails.Children.Clear();choices.Clear();pictures.Clear();visiblePictures.Clear();
            var items=all.Where(i=>(!pendingOnly || !Mounted(i)) && Matches(i)).OrderByDescending(Created).ToArray();
            selected.IntersectWith(items.Select(i=>i.Path));
            foreach(var item in items)AddChoice(item);
            Dispatcher.BeginInvoke(new Action(UpdateThumbnails), System.Windows.Threading.DispatcherPriority.Loaded);
            empty.Text=all.Count==0?"篮子还是空的\n新建备忘或粘贴内容开始记录":"没有匹配的备忘";
            empty.Visibility=items.Length==0?Visibility.Visible:Visibility.Collapsed;scroll.Visibility=items.Length==0?Visibility.Collapsed:Visibility.Visible;
            scroll.ScrollToVerticalOffset(offset);UpdateSelection();
            if(!creatingMemo) {
                var next=items.FirstOrDefault(i=>previewItem!=null && i.Path==previewItem.Path) ?? items.FirstOrDefault();
                if(next!=previewItem || (next!=null && !next.IsText && previewContent!=next.Image))ShowPreview(next);
                else ArchiveOption.SetKey(next==null?null:next.Path);
            }
        }

        void AddChoice(CaptureItem item)
        {
            bool mounted=Mounted(item);string note=MemoText(item);
            var stack=new StackPanel();var tile=new Grid {Height=96,Margin=new Thickness(6)};
            if(item.IsText)tile.Children.Add(new TextBlock {Text=note,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=90,FontSize=12,Foreground=LightTheme.Brush("#283343"),Margin=new Thickness(4)});
            else {var image=new Image {Stretch=Stretch.Uniform};pictures[item]=image;tile.Children.Add(image);}
            var mark=new Border {Width=22,Height=22,CornerRadius=new CornerRadius(11),Background=LightTheme.Brush("#2463AE"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Child=new TextBlock {Text="✓",Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            tile.Children.Add(mark);stack.Children.Add(tile);
            if(!item.IsText&&!string.IsNullOrEmpty(note))stack.Children.Add(new TextBlock {Text=note.Replace("\r"," ").Replace("\n"," "),TextTrimming=TextTrimming.CharacterEllipsis,FontSize=11,Margin=new Thickness(7,0,7,0)});
            stack.Children.Add(new TextBlock {Text=TimeLabel(Created(item)),Foreground=LightTheme.Brush("#8390A1"),FontSize=10,Margin=new Thickness(7,5,7,0)});
            var frame=new Border {Child=stack,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(2)};
            var button=new Button {Content=frame,Template=choiceTemplate,Height=150,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(0),Margin=new Thickness(4,0,4,10),HorizontalContentAlignment=HorizontalAlignment.Stretch};
            Action sync=delegate {bool chosen=selected.Contains(item.Path);mark.Visibility=chosen?Visibility.Visible:Visibility.Collapsed;frame.BorderBrush=LightTheme.Brush(chosen?"#2463AE":"#E5EAF1");frame.Background=LightTheme.Brush(chosen?"#EDF4FF":"#FFFFFF");AutomationProperties.SetItemStatus(button,chosen?(mounted?"已选择，已挂上":"已选择"):mounted?"已挂上":"待挂");};
            button.Click+=delegate {if(!SaveMemo())return;if(!selected.Add(item.Path))selected.Remove(item.Path);ShowPreview(item);sync();UpdateSelection();};
            var menu=new ImageMenu(delegate(ShotAction action){if(SaveMemo())act(action,item);});menu.SetCard(item.IsText,!string.IsNullOrEmpty(note));menu.ChooseColor=delegate(int color){try{gallery.Store.SetCardColor(item.Path,color);gallery.Notify();}catch(Exception error){gallery.Store.Log(error);status.Text="颜色保存失败，请重试";}};button.ContextMenu=menu;
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

        async void ShowPreview(CaptureItem item)
        {
            if(previewItem!=item && !SaveMemo())return;
            int version=++previewVersion;bool changed=previewItem!=item;
            previewItem=item;previewContent=item==null?null:item.Image;
            ArchiveOption.SetKey(item==null?null:item.Path);
            if(changed || !MemoEditor.IsEnabled)LoadMemo(item);
            preview.Visibility=item==null || item.IsText?Visibility.Collapsed:Visibility.Visible;
            preview.Source=item==null || item.IsText?null:item.Image.CachedThumbnail;
            previewHint.Visibility=item==null?Visibility.Visible:Visibility.Collapsed;previewHint.Text="选择备忘查看详情";
            var parent=(Grid)preview.Parent;parent.RowDefinitions[1].Height=new GridLength(item==null || item.IsText?0:150);
            metadata.Text=item==null?"":MemoType(item)+" · "+TimeLabel(Created(item));
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
                    double width = Math.Max(1, (panel.ActualWidth - preview.Margin.Left - preview.Margin.Right) * scale);
                    double height = Math.Max(1, (panel.RowDefinitions[1].ActualHeight - preview.Margin.Top - preview.Margin.Bottom) * scale);
                    var picture = await Task.Run(() => ReadPreview(item.Path, content, width, height));
                    if (version == previewVersion) { preview.Source = picture; previewHint.Visibility = Visibility.Collapsed; }
                }
                finally { previewSlot.Release(); }
            }
            catch (IOException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "图片已移动或删除"; previewHint.Visibility = Visibility.Visible; } }
            catch (UnauthorizedAccessException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "暂时无法读取这张图片"; previewHint.Visibility = Visibility.Visible; } }
            catch (NotSupportedException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "暂时无法预览这张图片"; previewHint.Visibility = Visibility.Visible; } }
            catch (FormatException) { if (version == previewVersion) { preview.Source = null; previewHint.Text = "暂时无法预览这张图片"; previewHint.Visibility = Visibility.Visible; } }
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
            int pending=AllMemos().Count(i=>selected.Contains(i.Path)&&!Mounted(i));
            status.Text = "已选 " + selected.Count + " 条" + (pending < selected.Count ? " · " + pending + " 条待挂" : "");
            HangButton.IsEnabled = pending > 0;
            DeleteButton.IsEnabled = selected.Count > 0;
        }

        void Mount()
        {
            if(!SaveMemo())return;
            var items=AllMemos().Where(i=>selected.Contains(i.Path)&&!Mounted(i)).ToArray();
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
