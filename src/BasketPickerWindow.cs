using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ScreenLine
{
    internal sealed class BasketPickerWindow : Window
    {
        readonly Gallery gallery;
        readonly CaptureItem[] pictures;
        readonly TextItem[] texts;
        readonly HashSet<CaptureItem> chosenPictures;
        readonly HashSet<TextItem> chosenTexts;
        readonly UniformGrid pictureGrid = new UniformGrid { Columns = 2 };
        readonly StackPanel textList = new StackPanel();
        readonly Dictionary<Image, CaptureItem> thumbnails = new Dictionary<Image, CaptureItem>();
        readonly ScrollViewer pictureScroll;
        readonly ScrollViewer textScroll;
        readonly TextBlock count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        bool loadingThumbnails, closed;
        bool refreshThumbnails;
        public CaptureItem[] Pictures { get { return pictures.Where(chosenPictures.Contains).ToArray(); } }
        public TextItem[] Texts { get { return texts.Where(chosenTexts.Contains).ToArray(); } }

        internal BasketPickerWindow(Gallery gallery, CaptureItem original, CaptureItem[] selectedPictures, TextItem[] selectedTexts)
        {
            this.gallery = gallery;
            pictures = gallery.Collected.Where(item => item.Path != original.Path).ToArray();
            texts = gallery.Store.State.Texts.Where(item => original.Text == null || item.Id != original.Text.Id).ToArray();
            chosenPictures = new HashSet<CaptureItem>(selectedPictures.Where(pictures.Contains));
            chosenTexts = new HashSet<TextItem>(selectedTexts.Where(texts.Contains));

            Title = "从篮子选择";
            Width = 790;
            Height = 580;
            MinWidth = 560;
            MinHeight = 370;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Foreground = LightTheme.Brush("#242821");
            var root = new DockPanel { Margin = new Thickness(22, 16, 22, 14) };
            Content = new Border { Child = root, CornerRadius = new CornerRadius(12),
                Background = LightTheme.Brush("#FCFBF8"), BorderBrush = LightTheme.Brush("#DDDAD2"),
                BorderThickness = new Thickness(1) };
            SourceInitialized += delegate {
                if (Owner != null && Owner.ActualWidth > 0 && Owner.ActualHeight > 0) {
                    Width = Math.Min(960, Math.Max(MinWidth, Owner.ActualWidth - 54));
                    Height = Math.Min(710, Math.Max(MinHeight, Owner.ActualHeight - 48));
                    Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
                    Top = Owner.Top + (Owner.ActualHeight - Height) / 2;
                }
            };

            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = Title, FontSize = 29, FontWeight = FontWeights.Bold });
            titles.Children.Add(new TextBlock { Text = "选择要加入当前便签的图片和文字", Foreground = LightTheme.Brush("#666960"),
                Margin = new Thickness(0, 4, 0, 0) });
            header.Children.Add(titles);
            var search = new TextBox { Padding = new Thickness(8, 4, 8, 4), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            AutomationProperties.SetName(search, "搜索文字或图片名称");
            var searchBox = new Grid();
            searchBox.Children.Add(search);
            var hint = new TextBlock { Text = "搜索文字或图片名称", Foreground = LightTheme.Brush("#666960"), Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            searchBox.Children.Add(hint);
            var searchRow = new DockPanel();
            var magnifier = new TextBlock { Text = "\uE721", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            searchRow.Children.Add(magnifier);
            searchRow.Children.Add(searchBox);
            var searchFrame = new Border { Child = searchRow, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
                BorderBrush = LightTheme.Brush("#DDDAD2"), VerticalAlignment = VerticalAlignment.Bottom };
            var searchAndClose = new DockPanel();
            var close = LightTheme.Button("", () => DialogResult = false, "quiet");
            close.Content = LightTheme.CloseIcon();
            close.Width = close.Height = 26;
            close.MinWidth = 0;
            close.Padding = new Thickness(0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetName(close, "关闭选择窗");
            DockPanel.SetDock(close, Dock.Top);
            searchAndClose.Children.Add(close);
            searchAndClose.Children.Add(searchFrame);
            Grid.SetColumn(searchAndClose, 1);
            header.Children.Add(searchAndClose);
            var headerRule = new Border { Child = header, BorderBrush = LightTheme.Brush("#E5E2DB"),
                BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(headerRule, Dock.Top);
            root.Children.Add(headerRule);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = LightTheme.Button("取消", () => DialogResult = false);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
            var confirm = LightTheme.Button("加入便签", () => DialogResult = true);
            confirm.Background = confirm.BorderBrush = LightTheme.Brush("#244D36");
            confirm.Foreground = Brushes.White;
            confirm.IsDefault = true;
            buttons.Children.Add(confirm);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            footer.Children.Add(count);
            var footerRule = new Border { Child = footer, BorderBrush = LightTheme.Brush("#E5E2DB"),
                BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(footerRule, Dock.Bottom);
            root.Children.Add(footerRule);

            var panes = new Grid();
            panes.ColumnDefinitions.Add(new ColumnDefinition());
            panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            panes.ColumnDefinitions.Add(new ColumnDefinition());
            pictureScroll = CreatePane(panes, 0, "\uEB9F", "图片", pictureGrid);
            textScroll = CreatePane(panes, 2, "\uE8A5", "文字", textList);
            var divider = new Border { Width = 1, Background = LightTheme.Brush("#E5E2DB") };
            Grid.SetColumn(divider, 1);
            panes.Children.Add(divider);
            root.Children.Add(panes);
            pictureScroll.ScrollChanged += delegate { RequestThumbnails(); };
            Loaded += delegate { RequestThumbnails(); };
            Closed += delegate { closed = true; thumbnails.Clear(); };
            search.TextChanged += delegate {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Populate(search.Text.Trim());
                pictureScroll.ScrollToTop();
                textScroll.ScrollToTop();
            };
            Populate("");
            UpdateCount();
        }

        static ScrollViewer CreatePane(Grid parent, int column, string glyph, string title, Panel items)
        {
            var pane = new DockPanel();
            var heading = LightTheme.IconLabel(glyph, title);
            TextBlock.SetFontSize(heading, 17);
            TextBlock.SetFontWeight(heading, FontWeights.SemiBold);
            heading.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(heading, Dock.Top);
            pane.Children.Add(heading);
            var scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            pane.Children.Add(scroll);
            Grid.SetColumn(pane, column);
            parent.Children.Add(pane);
            return scroll;
        }

        void Populate(string query)
        {
            pictureGrid.Children.Clear();
            textList.Children.Clear();
            thumbnails.Clear();
            foreach (var item in pictures) {
                string note = gallery.Note(item) ?? "";
                if (!Matches(Path.GetFileName(item.Path), query) && !Matches(note, query)) continue;
                string caption = note.Trim().Length > 0 ? LightTheme.MemoTitle(item, note) : "图片";
                var image = new Image { Height = 116, Stretch = Stretch.UniformToFill, Source = item.Image.CachedThumbnail };
                var thumbnail = new Grid { Height = 116, ClipToBounds = true, Background = LightTheme.Brush("#EEECE5") };
                thumbnail.Children.Add(image);
                thumbnails.Add(image, item);
                var content = new StackPanel();
                content.Children.Add(thumbnail);
                content.Children.Add(new TextBlock { Text = caption, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 5, 8, 0) });
                content.Children.Insert(1, DateLabel(gallery.Store.CardCreated(item.Path), new Thickness(8, 5, 8, 0)));
                content.Margin = new Thickness(0, 0, 0, 6);
                pictureGrid.Children.Add(Choice(content, caption + "，" + Path.GetFileName(item.Path), chosenPictures.Contains(item),
                    () => Toggle(chosenPictures, item)));
            }
            foreach (var item in texts) {
                string text = (item.Content ?? "").Trim();
                if (!Matches(text, query)) continue;
                int lineBreak = text.IndexOfAny(new[] { '\r', '\n' });
                string title = LightTheme.MemoTitle(new CaptureItem(item), text);
                string excerpt = lineBreak >= 0 ? text.Substring(lineBreak).Trim() : text.Length > 40 ? text.Substring(40) : "";
                var content = new StackPanel { Margin = new Thickness(10, 8, 29, 8) };
                content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (excerpt.Length > 0) content.Children.Add(new TextBlock { Text = excerpt.Replace('\r', ' ').Replace('\n', ' '),
                    TextTrimming = TextTrimming.CharacterEllipsis, Foreground = LightTheme.Brush("#555950"),
                    Margin = new Thickness(0, 3, 0, 0) });
                content.Children.Add(DateLabel(item.CreatedUtc, new Thickness(0, 3, 0, 0)));
                var choice = Choice(content, title, chosenTexts.Contains(item), () => Toggle(chosenTexts, item), true);
                choice.ToolTip = text;
                textList.Children.Add(choice);
            }
            if (pictureGrid.Children.Count == 0) pictureGrid.Children.Add(Empty("没有可选图片"));
            if (textList.Children.Count == 0) textList.Children.Add(Empty("没有可选文字"));
        }

        static bool Matches(string value, string query)
        {
            return value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        static TextBlock Empty(string message)
        {
            return new TextBlock { Text = message, Foreground = LightTheme.Brush("#666960"), Margin = new Thickness(4, 10, 0, 0) };
        }

        static TextBlock DateLabel(DateTime date, Thickness margin)
        {
            DateTime local = date.ToLocalTime();
            return new TextBlock { Text = local.ToString(local.Date == DateTime.Today ? "'今天' HH:mm" : "MM-dd HH:mm"),
                Foreground = LightTheme.Brush("#666960"), FontSize = 11, Margin = margin };
        }

        CheckBox Choice(FrameworkElement content, string name, bool selected, Action toggle, bool showEmptyMark = false)
        {
            var mark = new Border { Child = new TextBlock { Text = "✓", Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = LightTheme.Brush("#244D36"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
            mark.BorderThickness = new Thickness(1);
            var card = new Grid { ClipToBounds = true };
            card.Children.Add(content);
            card.Children.Add(mark);
            var choice = new CheckBox { Content = card, Template = ChoiceTemplate, IsChecked = selected,
                Margin = new Thickness(0, 0, 6, 6), Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetName(choice, name);
            Action refresh = delegate {
                bool isChecked = choice.IsChecked == true;
                mark.Visibility = isChecked || showEmptyMark ? Visibility.Visible : Visibility.Collapsed;
                mark.Background = isChecked ? LightTheme.Brush("#244D36") : Brushes.Transparent;
                mark.BorderBrush = isChecked ? Brushes.White : LightTheme.Brush("#A49E93");
                ((TextBlock)mark.Child).Text = isChecked ? "✓" : "";
            };
            RoutedEventHandler changed = delegate { toggle(); refresh(); UpdateCount(); };
            choice.Checked += changed;
            choice.Unchecked += changed;
            refresh();
            return choice;
        }

        static readonly ControlTemplate ChoiceTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
                <Border x:Name='Frame' BorderBrush='#DDDAD2' BorderThickness='1' CornerRadius='9' Background='#FCFBF8'>
                    <ContentPresenter/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#F1F4EE'/></Trigger>
                    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Frame' Property='Background' Value='#E4ECE1'/><Setter TargetName='Frame' Property='BorderBrush' Value='#244D36'/></Trigger>
                    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#244D36'/><Setter TargetName='Frame' Property='BorderThickness' Value='2'/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>");

        static void Toggle<T>(HashSet<T> selected, T item)
        {
            if (!selected.Add(item)) selected.Remove(item);
        }

        void UpdateCount()
        {
            count.Text = "已选 " + chosenPictures.Count + " 张图片 · " + chosenTexts.Count + " 条文字";
        }

        async void RequestThumbnails()
        {
            if (!IsLoaded || closed) return;
            refreshThumbnails = true;
            if (loadingThumbnails) return;
            loadingThumbnails = true;
            try {
                while (refreshThumbnails && !closed) {
                    refreshThumbnails = false;
                    var viewport = new Rect(0, 0, pictureScroll.ViewportWidth, pictureScroll.ViewportHeight);
                    foreach (var entry in thumbnails.ToArray()) {
                        if (closed || refreshThumbnails) break;
                        var image = entry.Key;
                        if (image.Source != null || !image.IsDescendantOf(pictureScroll)) continue;
                        if (!image.TransformToAncestor(pictureScroll).TransformBounds(new Rect(image.RenderSize)).IntersectsWith(viewport)) continue;
                        try {
                            var bitmap = await Task.Run(() => entry.Value.Image.Thumbnail);
                            if (!closed && thumbnails.ContainsKey(image)) image.Source = bitmap;
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (NotSupportedException) { }
                        catch (FormatException) { }
                    }
                }
            }
            finally { loadingThumbnails = false; }
        }
    }
}
