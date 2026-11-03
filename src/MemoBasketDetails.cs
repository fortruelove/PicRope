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
        internal readonly TextBox MemoEditor=new TextBox {AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12),FontSize=14,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
        internal CheckBox TextAutoHang;
        internal readonly Button[] TypeButtons=new Button[3];
        int memoTypeFilter;
        internal int ActiveType {get{return memoTypeFilter;}}
        void ChooseType(int type){if(!SaveMemo())return;memoTypeFilter=memoTypeFilter==type?0:type;Refresh();}
        internal TextBox Search;
        internal Button NewMemoButton;
        readonly DispatcherTimer memoTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(350)};
        bool loadingMemo,savingMemo,creatingMemo;

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
        void InitializeMemoEditor(Grid body) {
            MemoEditor.IsEnabled=false;System.Windows.Automation.AutomationProperties.SetName(MemoEditor,"备忘详情，自动保存");
            Grid.SetRow(MemoEditor,2);body.Children.Add(MemoEditor);
            MemoEditor.TextChanged+=delegate {if(!loadingMemo && (previewItem!=null || creatingMemo)){memoTimer.Stop();memoTimer.Start();}};
            memoTimer.Tick+=delegate {SaveMemo();};
        }
        void LoadMemo(CaptureItem item) {
            creatingMemo=false;loadingMemo=true;MemoEditor.Text=item==null?"":MemoText(item);MemoEditor.IsEnabled=item!=null;loadingMemo=false;
        }
        internal bool SaveMemo() {
            memoTimer.Stop();if(savingMemo)return true;
            if(creatingMemo){if(string.IsNullOrWhiteSpace(MemoEditor.Text))return true;}
            else if(previewItem==null || MemoEditor.Text==MemoText(previewItem))return true;
            try {
                savingMemo=true;
                if(creatingMemo){var text=texts.Create(MemoEditor.Text);previewItem=new CaptureItem(text);memoTexts[text.Id]=previewItem;creatingMemo=false;ArchiveOption.SetKey(previewItem.Path);}
                else if(previewItem.IsText)texts.UpdateContent(previewItem.Text,MemoEditor.Text);
                else {if(!gallery.Collected.Contains(previewItem))throw new InvalidOperationException("备忘已被移除。");gallery.SetNote(previewItem,MemoEditor.Text);}
                metadata.Text=MemoType(previewItem)+" · "+TimeLabel(Created(previewItem));
                status.Text="已自动保存";
                if(!closed)Dispatcher.BeginInvoke(new Action(delegate {if(!closed)Refresh();}));
                return true;
            }catch(Exception error){gallery.Store.Log(error);status.Text="保存失败，草稿已保留，请重试";return false;}
            finally {savingMemo=false;}
        }
        void NewMemo() {
            if(!SaveMemo())return;
            refreshing=true;Search.Text="";memoTypeFilter=0;pendingOnly=false;refreshing=false;
            creatingMemo=true;previewItem=null;previewContent=null;previewVersion++;selected.Clear();
            loadingMemo=true;MemoEditor.Text="";MemoEditor.IsEnabled=true;loadingMemo=false;
            preview.Source=null;preview.Visibility=Visibility.Collapsed;previewHint.Visibility=Visibility.Collapsed;
            ((Grid)preview.Parent).RowDefinitions[1].Height=new GridLength(0);ArchiveOption.SetKey(null);metadata.Text="新备忘 · 输入后自动保存";
            Refresh();MemoEditor.Focus();
        }
        void OpenOriginal() {
            if(!SaveMemo() || previewItem==null || previewItem.IsText)return;
            try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(previewItem.Path){UseShellExecute=true});if(popupShelf!=null)popupShelf.FollowImageViewer(previewItem.Path);}
            catch(Exception error){gallery.Store.Log(error);status.Text="无法打开原图："+error.Message;}
        }
        internal void FilterText(){if(!SaveMemo())return;pendingOnly=false;memoTypeFilter=2;Refresh();}
    }
}
