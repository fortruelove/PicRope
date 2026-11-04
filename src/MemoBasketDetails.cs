using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenLine {
    internal sealed partial class BasketWindow {
        readonly TextCollection texts;
        readonly Dictionary<string,CaptureItem> memoTexts=new Dictionary<string,CaptureItem>();
        internal readonly TextBox MemoEditor=new TextBox {AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0, 6, 0, 6),FontSize=16,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
        internal CheckBox TextAutoHang;
        internal readonly Button[] TypeButtons=new Button[3];
        int memoTypeFilter;
        internal int ActiveType {get{return memoTypeFilter;}}
        void ChooseType(int type){if(!SaveMemo())return;memoTypeFilter=memoTypeFilter==type?0:type;Refresh();}
        internal TextBox Search;
        internal Button NewMemoButton;
        readonly DispatcherTimer memoTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(350)};
        bool loadingMemo,savingMemo,creatingMemo;
        bool memoDirty;
        readonly TextBlock notePlaceholder = new TextBlock { Text = "点击输入文字…",
            Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 6, 0, 0), IsHitTestVisible = false };
        readonly TextBlock noteSaved = new TextBlock { Text = "已保存", Foreground = LightTheme.Brush("#65717F"),
            Visibility = Visibility.Collapsed };
        readonly DispatcherTimer noteSavedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        DockPanel noteHeader;
        Border noteSection;
        DockPanel editorArea;

        List<CaptureItem> AllMemos() {
            foreach(var text in texts.Items){CaptureItem card;if(!memoTexts.TryGetValue(text.Id,out card)||card.Text!=text)memoTexts[text.Id]=new CaptureItem(text);}
            var valid=new HashSet<string>(texts.Items.Select(t=>t.Id));
            foreach(var id in memoTexts.Keys.Where(id=>!valid.Contains(id)).ToArray())memoTexts.Remove(id);
            return gallery.Collected.Concat(texts.Items.Select(t=>memoTexts[t.Id])).ToList();
        }
        bool Mounted(CaptureItem item){return item.IsText?item.Text.Hanging:gallery.Contains(item.Path);}
        string MemoText(CaptureItem item){return item.IsText?item.Text.Content:gallery.Note(item);}
        string MemoType(CaptureItem item){return item.IsText?"文字":string.IsNullOrEmpty(gallery.Note(item))?"图片":"图文";}
        DateTime Created(CaptureItem item){if(item.IsText)return item.Text.CreatedUtc;DateTime date;if(gallery.Store.State.CardCreatedUtc!=null&&gallery.Store.State.CardCreatedUtc.TryGetValue(item.Path,out date))return date;return gallery.Store.CardCreated(item.Path);}
        bool Matches(CaptureItem item) {
            int type=memoTypeFilter;
            if(type==1&&(item.IsText||!string.IsNullOrEmpty(gallery.Note(item))))return false;
            if(type==2&&!item.IsText)return false;
            if(type==3&&(item.IsText||string.IsNullOrEmpty(gallery.Note(item))))return false;
            string term=Search.Text.Trim();return term.Length==0 || (MemoText(item)+(item.IsText?"":" "+Path.GetFileName(item.Path))).IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0;
        }
        void ChangeTextAutoHang(){if(refreshing)return;try{texts.AutoHang=TextAutoHang.IsChecked==true;}catch(Exception error){gallery.Store.Log(error);refreshing=true;TextAutoHang.IsChecked=texts.AutoHang;refreshing=false;status.Text="文字自动上绳设置保存失败";}}
        void InitializeMemoEditor(Panel body) {
            MemoEditor.IsEnabled=false;System.Windows.Automation.AutomationProperties.SetName(MemoEditor,"备忘详情，自动保存");
            editorArea = new DockPanel();
            noteHeader = new DockPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(noteSaved, Dock.Right);
            noteHeader.Children.Add(noteSaved);

            DockPanel.SetDock(noteHeader, Dock.Bottom);
            editorArea.Children.Add(noteHeader);

            var input = new Grid();
            input.Children.Add(MemoEditor);
            notePlaceholder.Visibility = Visibility.Collapsed;
            input.Children.Add(notePlaceholder);
            input.MinHeight = 58;
            editorArea.Children.Add(input);
            noteSection = new Border { Child = editorArea, CornerRadius = new CornerRadius(8),
                Visibility = Visibility.Collapsed };
            body.Children.Add(noteSection);
            MemoEditor.PreviewMouseWheel += delegate(object sender, System.Windows.Input.MouseWheelEventArgs e) {
                if (detailScroll.ScrollableHeight <= 0) return;
                var wheel = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) {
                    RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent
                };
                detailScroll.RaiseEvent(wheel);
                e.Handled = wheel.Handled;
            };
            noteSavedTimer.Tick += delegate { ClearNoteSaved(); };
            Closed += delegate { noteSavedTimer.Stop(); };
            MemoEditor.TextChanged+=delegate {UpdateNotePlaceholder(); if(!loadingMemo && (previewItem!=null || creatingMemo)){ClearNoteSaved();memoDirty=true;memoTimer.Stop();memoTimer.Start();}};
            memoTimer.Tick+=delegate {SaveMemo();};
        }
        void ClearNoteSaved() {
            noteSavedTimer.Stop();
            noteSaved.Visibility = Visibility.Collapsed;
        }
        void UpdateNotePlaceholder() {
            notePlaceholder.Visibility = noteHeader.Visibility == Visibility.Visible && MemoEditor.Text.Length == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        void SetNoteMode(bool hasEditor) {
            ClearNoteSaved();

            noteSection.Visibility = hasEditor ? Visibility.Visible : Visibility.Collapsed;
            noteHeader.Visibility = noteSection.Visibility;
            System.Windows.Automation.AutomationProperties.SetName(MemoEditor,
                "文字，自动保存");
            UpdateNotePlaceholder();
        }
        void LoadMemo(CaptureItem item) {
            SetNoteMode(item != null);
            creatingMemo=false;loadingMemo=true;MemoEditor.Text=item==null?"":MemoText(item);MemoEditor.IsEnabled=item!=null;loadingMemo=false;memoDirty=false;
        }
        internal bool SaveMemo() {
            memoTimer.Stop();if(savingMemo)return true;
            if(creatingMemo){if(string.IsNullOrWhiteSpace(MemoEditor.Text))return true;}
            else {
                if (previewItem == null) return true;
                bool exists = previewItem.IsText
                    ? texts.Items.Contains(previewItem.Text)
                    : gallery.Collected.Contains(previewItem);
                if (!exists) {
                    ShowPreview(null, saveCurrent: false);
                    return true;
                }
                if (!memoDirty || MemoEditor.Text == MemoText(previewItem)) { memoDirty = false; return true; }
            }
            try {
                savingMemo=true;
                if(creatingMemo){var text=texts.Create(MemoEditor.Text);previewItem=new CaptureItem(text);memoTexts[text.Id]=previewItem;creatingMemo=false;ArchiveOption.SetKey(previewItem.Path);}
                else if(previewItem.IsText)texts.UpdateContent(previewItem.Text,MemoEditor.Text);
                else gallery.SetNote(previewItem,MemoEditor.Text);
                documentTitle.Text = LightTheme.MemoTitle(previewItem, MemoEditor.Text);
                metadata.Text=MemoType(previewItem)+" · "+TimeLabel(Created(previewItem));
                noteSaved.Visibility = Visibility.Visible;
                status.Text = "● 已保存";
                noteSavedTimer.Start();
                memoDirty = false;
                if(!closed)Dispatcher.BeginInvoke(new Action(delegate {if(!closed)Refresh();}));
                return true;
            }catch(Exception error){gallery.Store.Log(error);status.Text="保存失败，草稿已保留，请重试";return false;}
            finally {savingMemo=false;}
        }
        void NewMemo() {
            if(!SaveMemo())return;
            refreshing=true;Search.Text="";memoTypeFilter=0;pendingOnly=false;refreshing=false;
            creatingMemo=true;previewItem=null;previewContent=null;previewVersion++;selected.Clear();
            memoDirty = false;
            pictureSection.Visibility = Visibility.Collapsed;
            SetNoteMode(true);
            loadingMemo=true;MemoEditor.Text="";MemoEditor.IsEnabled=true;loadingMemo=false;
            preview.Source=null;preview.Visibility=Visibility.Collapsed;previewHint.Visibility=Visibility.Collapsed;
            previewPaths = new string[0];
            UpdatePictureRemoval();
            previewTiles.Children.Clear();
            pictureHover.Dismiss();
            detailScroll.ScrollToTop();
            ArchiveOption.SetKey(null);documentTitle.Text = "新备忘"; metadata.Text="输入后自动保存";
            Refresh();MemoEditor.Focus();
        }
        void CombineSelection()
        {
            if (!SaveMemo()) return;
            var cards = visibleMemos.Where(card => selected.Contains(card.Path)).ToArray();
            if (cards.Length <= 1) {
                var item = cards.FirstOrDefault() ?? previewItem;
                if (item != null) act(ShotAction.CardEdit, item);
                return;
            }
            try {
                CaptureItem combined;
                mounting = true;
                try { combined = gallery.Combine(cards); }
                finally { mounting = false; }
                if (!Matches(combined)) {
                    refreshing = true;
                    memoTypeFilter = 0;
                    Search.Clear();
                    refreshing = false;
                }
                selected.Clear();
                selected.Add(combined.Path);
                Refresh();
                if (visibleMemos.Contains(combined)) ShowPreview(combined);
                status.Text = "已组合 " + cards.Length + " 条备忘";
            } catch (Exception error) {
                gallery.Store.Log(error);
                status.Text = "组合未完成：" + error.Message;
            }
        }

        void OpenOriginal() {
            if(previewItem!=null && !previewItem.IsText) OpenPreviewPicture(previewItem.Path);
        }
        void OpenPreviewPicture(string path) {
            if (!SaveMemo() || previewItem == null || !previewPaths.Contains(path)) return;
            try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});if(popupShelf!=null)popupShelf.FollowImageViewer(path);}
            catch(Exception error){gallery.Store.Log(error);status.Text="无法打开原图："+error.Message;}
        }
        internal void FilterText(){if(!SaveMemo())return;pendingOnly=false;memoTypeFilter=2;Refresh();}
    }
}
