using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ScreenLine {
// Changes stay in this dialog until Save; cancelling never changes the collection.
internal sealed class CardEditorWindow : Window {
    public readonly TextBox Body = new TextBox { AcceptsReturn=true, AcceptsTab=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, MinHeight=120, Padding=new Thickness(10) };
    public CaptureItem SelectedPicture;
    public TextItem SelectedText;
    public ClipboardPicture NewPicture;
    public bool Append;
    readonly Image preview = new Image {Height=150,Stretch=Stretch.Uniform,Margin=new Thickness(0,8,0,8)};
    readonly TextBlock feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=LightTheme.Brush("#65717F"),Margin=new Thickness(0,8,0,8)};
    readonly CheckBox append=new CheckBox {Content="追加到原备注（取消勾选则替换）",IsChecked=true,Margin=new Thickness(0,6,0,6)};
    readonly CaptureItem original;
    public CardEditorWindow(Gallery gallery, CaptureItem item, bool readOnly) {
        original=item; Title=readOnly ? "查看内容卡片" : "关联 / 编辑图文";
        Width=600; Height=720; MinWidth=440; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Topmost=true; FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=13;
        Background=Brushes.White; Foreground=LightTheme.Brush("#283343");
        var root=new DockPanel {Margin=new Thickness(20),Background=Brushes.White}; Content=root;
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var save=LightTheme.Button(readOnly?"关闭":"保存",delegate {Append=append.IsChecked==true; DialogResult=true;});
        actions.Children.Add(save);
        if(!readOnly) actions.Children.Add(LightTheme.Button("取消",delegate {DialogResult=false;},"quiet"));
        DockPanel.SetDock(actions,Dock.Bottom); root.Children.Add(actions);
        var scroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto}; root.Children.Add(scroll);
        var panel=new StackPanel(); scroll.Content=panel;
        panel.Children.Add(new TextBlock {Text=readOnly?"完整内容":"一张卡片，一起挂绳和管理",FontSize=18,FontWeight=FontWeights.SemiBold});
        if(!item.IsText) preview.Source=item.Image.Thumbnail;
        panel.Children.Add(preview);
        Body.Text=item.IsText ? item.Text.Content : gallery.Note(item); Body.IsReadOnly=readOnly;
        if(!readOnly) {
            var pictureTools=new WrapPanel();
            pictureTools.Children.Add(LightTheme.Button("选择本地图片…",ChooseFile));
            pictureTools.Children.Add(LightTheme.Button("粘贴图片",async delegate {await PastePicture();}));
            panel.Children.Add(pictureTools);
            panel.Children.Add(new TextBlock {Text="或从图片篮子选择（与当前卡片合并）",Margin=new Thickness(0,8,0,4)});
            var pictures=new ComboBox {ItemsSource=gallery.Collected.Where(p=>p!=item).ToArray(),DisplayMemberPath="Label",MaxDropDownHeight=240};
            pictures.SelectionChanged+=delegate {SelectedPicture=pictures.SelectedItem as CaptureItem; if(SelectedPicture!=null){NewPicture=null; preview.Source=SelectedPicture.Image.Thumbnail; feedback.Text="保存后合并已有图片，沿用当前卡片的挂绳状态。";}};
            panel.Children.Add(pictures);
            if(!item.IsText) {
                panel.Children.Add(new TextBlock {Text="从文字信封选择（保存后合并）",Margin=new Thickness(0,8,0,4)});
                var texts=new ComboBox {ItemsSource=gallery.Store.State.Texts.ToArray(),DisplayMemberPath="Content",MaxDropDownHeight=240};
                texts.SelectionChanged+=delegate {SelectedText=texts.SelectedItem as TextItem; if(SelectedText!=null)Body.Text=SelectedText.Content;}; panel.Children.Add(texts);
                append.Visibility=string.IsNullOrEmpty(gallery.Note(item)) ? Visibility.Collapsed : Visibility.Visible;
                panel.Children.Add(append);
            }
        }
        panel.Children.Add(new TextBlock {Text=item.IsText?"文字 / 图片备注":"图片备注",Margin=new Thickness(0,8,0,6)});
        panel.Children.Add(Body); panel.Children.Add(feedback);
        if(!readOnly) feedback.Text="粘贴或选择的新图片仅在保存后收进篮子。文字保留完整换行和缩进。";
    }
    void ChooseFile() {
        var dialog=new OpenFileDialog {Title="选择配图",Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"};
        if(dialog.ShowDialog(this)!=true)return;
        try {var bytes=File.ReadAllBytes(dialog.FileName); var image=ImageContent.Decode(bytes); NewPicture=new ClipboardPicture{Bytes=bytes,Extension=Path.GetExtension(dialog.FileName)}; SelectedPicture=null; preview.Source=image.Thumbnail; feedback.Text="已选择配图，保存后生效。";}
        catch(Exception error){feedback.Text="无法读取图片："+error.Message;}
    }
    async Task PastePicture() {
        try {var card=await CardClipboard.Read(WindowsNative.GetClipboardSequenceNumber(),true); if(card==null || card.Picture==null){feedback.Text="剪贴板中没有图片。";return;}
            NewPicture=card.Picture; SelectedPicture=null; preview.Source=ImageContent.Decode(NewPicture.Bytes).Thumbnail;
            if(string.IsNullOrWhiteSpace(Body.Text) && !string.IsNullOrWhiteSpace(card.Text)) Body.Text=card.Text;
            feedback.Text="已粘贴配图，保存后生效。";
        } catch(Exception error){feedback.Text="粘贴未完成："+error.Message;}
    }
}
}
