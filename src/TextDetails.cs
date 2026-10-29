using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenLine
{
    internal sealed partial class TextWindow
    {
        internal readonly TextBox DetailEditor = new TextBox();
        readonly TextBlock detailTime = new TextBlock { FontSize = 12, Foreground = LightTheme.Brush("#8390A1"), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock detailStatus = new TextBlock { Text = "选择左侧文字查看详情", FontSize = 12, Foreground = LightTheme.Brush("#65717F") };
        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        TextItem detailItem;
        bool loadingDetail, savingDetail, creatingText;

        void NewText()
        {
            if (!SaveDetails()) return;
            Search.Text = "";
            refreshing = true; List.SelectedItem = null; refreshing = false;
            detailItem = null; creatingText = true; loadingDetail = true;
            DetailEditor.Text = ""; DetailEditor.IsEnabled = true;
            detailTime.Text = "新文字"; detailStatus.Text = "直接输入文字，内容将自动保存";
            loadingDetail = false; DetailEditor.Focus();
        }

        FrameworkElement CreateDetails()
        {
            var panel = new Grid { Margin = new Thickness(18, 10, 20, 12) };
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            panel.RowDefinitions.Add(new RowDefinition());
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var heading = new Grid();
            heading.Children.Add(new TextBlock { Text = "详情", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center });
            detailTime.HorizontalAlignment = HorizontalAlignment.Right; heading.Children.Add(detailTime); panel.Children.Add(heading);
            DetailEditor.AcceptsReturn = true; DetailEditor.AcceptsTab = true; DetailEditor.TextWrapping = TextWrapping.Wrap;
            DetailEditor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; DetailEditor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            DetailEditor.Background = Brushes.Transparent; DetailEditor.BorderThickness = new Thickness(0); DetailEditor.Padding = new Thickness(14);
            DetailEditor.FontSize = 14; DetailEditor.Foreground = LightTheme.Brush("#283343"); DetailEditor.IsEnabled = false;
            AutomationProperties.SetName(DetailEditor, "文字详情，自动保存");
            var editor = new Border { Background = LightTheme.Brush("#F5F7FA"), CornerRadius = new CornerRadius(8), Child = DetailEditor };
            Grid.SetRow(editor, 1); panel.Children.Add(editor);
            detailStatus.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetRow(detailStatus, 2); panel.Children.Add(detailStatus);
            DetailEditor.TextChanged += delegate
            {
                if (loadingDetail || (detailItem == null && !creatingText)) return;
                detailStatus.Text = "正在编辑…"; saveTimer.Stop(); saveTimer.Start();
            };
            saveTimer.Tick += delegate { SaveDetails(); };
            List.SelectionChanged += delegate
            {
                if (refreshing) return;
                var next = List.SelectedItem as TextItem;
                if (next == detailItem && !creatingText) return;
                if (!SaveDetails())
                {
                    refreshing = true; List.SelectedItem = detailItem; refreshing = false; return;
                }
                LoadDetails(next);
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!SaveDetails()) e.Cancel = true; };
            Closed += delegate { saveTimer.Stop(); };
            return new Border { BorderBrush = LightTheme.Brush("#E8EBEF"), BorderThickness = new Thickness(1, 0, 0, 0), Child = panel };
        }

        void LoadDetails(TextItem item)
        {
            if (detailItem == item && !creatingText) return;
            creatingText = false;
            detailItem = item; loadingDetail = true;
            DetailEditor.Text = item == null ? "" : item.Content;
            DetailEditor.IsEnabled = item != null;
            detailTime.Text = item == null ? "" : item.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm");
            detailStatus.Text = item == null ? "选择左侧文字查看详情" : "编辑后自动保存";
            loadingDetail = false;
        }

        bool SaveDetails()
        {
            saveTimer.Stop();
            if (savingDetail) return true;
            if (creatingText) { if (string.IsNullOrWhiteSpace(DetailEditor.Text)) return true; }
            else if (detailItem == null || !texts.Items.Contains(detailItem) || DetailEditor.Text == detailItem.Content) return true;
            bool saved = false, created = false;
            try
            {
                savingDetail = true;
                if (creatingText)
                {
                    detailItem = texts.Create(DetailEditor.Text); creatingText = false; created = true;
                    detailTime.Text = detailItem.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm");
                }
                else texts.UpdateContent(detailItem, DetailEditor.Text);
                saved = true;
                detailStatus.Text = "已自动保存";
                return true;
            }
            catch (Exception error)
            {
                texts.Store.Log(error); detailStatus.Text = "保存失败，编辑内容已保留，可继续编辑后重试";
                ShowFeedback("保存失败，请重试"); return false;
            }
            finally
            {
                savingDetail = false;
                if (saved && (created || Search.Text.Trim().Length > 0) && !closed)
                    Dispatcher.BeginInvoke(new Action(delegate { if (!closed) Refresh(); }));
            }
        }
    }
}
