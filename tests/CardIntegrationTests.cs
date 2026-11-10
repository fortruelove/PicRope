using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenLine.Tests {
internal static class CardIntegrationTests {
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindow(string type,string title);
[System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
static string root; static int passed;
static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("通过："+name);}
[STAThread] public static void Main(){
var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};LightTheme.Install(app);
app.Startup+=async delegate {DesktopController c=null; try {
root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","cards-ui-"+Guid.NewGuid().ToString("N"));
var store=new StateStore(root);c=new DesktopController(store,Path.Combine(root,"screenshots"),false);await c.Start();
((System.Windows.Threading.DispatcherTimer)typeof(ShelfWindow).GetField("pointer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(c.Shelf)).Stop();
var trayButtons=(Panel)c.TrayMenu.BasketToggleButton.Parent;
Check(trayButtons.Children.IndexOf(c.TrayMenu.BasketToggleButton)==trayButtons.Children.IndexOf(c.TrayMenu.ScreenButton)+1,"显示篮子位于显示屏幕下方");
c.ShowTrayMenu();await Task.Delay(100);c.TrayMenu.BasketToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(120);
Check(c.Basket!=null&&c.Basket.IsVisible,"托盘显示篮子入口打开统一篮子");
c.ShowTrayMenu();await Task.Delay(120);Check(c.Basket!=null,"打开托盘菜单时保留篮子以便切换关闭");
c.TrayMenu.BasketToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);Check(c.Basket==null,"再次点击托盘显示篮子入口关闭篮子");
Check(!store.State.AutoClipboard && c.Shelf.BasketButton.IsVisible && !c.Shelf.TextButton.IsVisible,"自动获取关闭时仍可进入统一篮子手动粘贴");
Check(!Find(c.Shelf,"截图会挂在这里"),"空绳不显示截图引导框");
c.TrayMenu.PinRope.IsChecked=true;c.Shelf.Reveal(false,false,0);
Check(c.Shelf.Revealed && new StateStore(root).State.KeepRopeVisible,"置顶后立即展示，阻止收起并记住设置");
c.Shelf.Capturing=true;c.Shelf.Reveal(false,false,0);Check(!c.Shelf.Revealed,"截图时临时隐藏置顶绳子");c.Shelf.Capturing=false;c.Shelf.Reveal(true,false,0);
c.TrayMenu.PinRope.IsChecked=false;c.Shelf.Reveal(false,false,0);Check(!c.Shelf.Revealed && !new StateStore(root).State.KeepRopeVisible,"取消置顶后允许恢复收起");c.Shelf.Reveal(true,true,0);
c.TrayMenu.Pictures.IsChecked=true;Check(store.State.CollectClipboard && !store.State.CollectTextClipboard && c.TrayMenu.Text.Visibility==Visibility.Visible,"图片和文字自动获取开关独立显示");
c.TrayMenu.Text.IsChecked=true;c.TrayMenu.Pictures.IsChecked=false;Check(!store.State.CollectClipboard && new StateStore(root).State.CollectTextClipboard,"仅文字开关持久保存，不打开图片获取");c.TrayMenu.Text.IsChecked=false;
Check(c.Shelf.BasketButton.RenderSize.Width*c.Shelf.BasketButton.LayoutTransform.Value.M11<=72.1,"统一篮子保留紧凑的点击范围");
await c.AcceptCard(new ClipboardCard {Text="可以挂绳的文字\n保留完整换行与缩进"});
Check(c.Texts.Items.Count==1 && c.Gallery.Hanging.Count==1 && c.Shelf.CardFor("text:"+c.Texts.Items[0].Id)!=null,"文字进入统一篮子并显示在绳子");
var pixels=new byte[240*140*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=170;pixels[i+1]=110;pixels[i+2]=40;pixels[i+3]=255;}
var image=BitmapSource.Create(240,140,96,96,PixelFormats.Bgra32,null,pixels,960);
var combined=new ClipboardCard {Text="图片下方的备注摘要：完整备注仍然保留。",Picture=new ClipboardPicture{Bytes=ImageContent.EncodeClipboard(image),Extension=".png"}};
await c.AcceptCard(combined);Check(c.Gallery.Collected.Count==1 && c.Texts.Items.Count==1 && c.Gallery.Hanging.Count==2,"一次图文只创建一张组合卡片");
c.Shelf.Update();c.Shelf.Reveal(true,true,0); await Task.Delay(180);
Render((FrameworkElement)c.Shelf.Content,"rope.png");
bool openedEditable = false;
bool stagedTextVisible = false;
app.Dispatcher.BeginInvoke(new Action(delegate {
    var editor = app.Windows.OfType<CardEditorWindow>().FirstOrDefault(window => window.IsVisible);
    if (editor == null) return;
    try {
        openedEditable = !editor.Body.IsReadOnly;
        Render((FrameworkElement)editor.Content, "editor.png");
        app.Dispatcher.BeginInvoke(new Action(delegate {
            var picker = app.Windows.OfType<BasketPickerWindow>().FirstOrDefault(window => window.IsVisible);
            if (picker == null) return;
            try {
                var choice = FindControl(picker, control => control is CheckBox) as CheckBox;
                if (choice != null) choice.IsChecked = true;
                var confirm = FindControl(picker, control => control is Button && Equals(((Button)control).Content, "加入便签")) as Button;
                if (confirm != null) confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            } finally { if (picker.IsVisible) picker.Close(); }
        }));
        var browse = FindControl(editor, control => control is Button && Equals(((Button)control).Content, "从篮子选择…")) as Button;
        if (browse != null) browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        editor.UpdateLayout();
        stagedTextVisible = editor.SelectedTexts.Length == 1 && Find(editor, c.Texts.Items[0].Content);
    } finally { editor.Close(); }
}));
c.Act(ShotAction.Open, c.Gallery.Collected[0]);
Check(openedEditable, "打开图文便签直接进入可编辑窗口");
Check(stagedTextVisible && c.Texts.Items.Count == 1 && c.Gallery.Note(c.Gallery.Collected[0]) == combined.Text, "从篮子选择的文字立即预览，关闭编辑窗不提交");
c.ShowBasket();await Task.Delay(180);Check(Find(c.Basket,"粘贴") && Find(c.Basket,"组合"),"篮子提供粘贴和关联入口");
c.ShowBasket();Check(c.Basket==null && !c.Shelf.ChoosingBasket,"再次点击篮子入口关闭弹出层");
c.ShowBasket();await Task.Delay(180);
var basketOrigin=c.Basket.PointToScreen(new Point());var basketArea=c.Shelf.Display.WorkingArea;
Check(basketOrigin.X>=basketArea.Left && basketOrigin.X+c.Basket.ActualWidth*WindowsNative.MonitorScale(c.Shelf.Display)<=basketArea.Right+1 && basketOrigin.Y>=c.Shelf.BasketAnchor.Y-1,"篮子在自身入口下方弹出且保持屏幕内");
c.ShowTexts();await Task.Delay(120);Check(c.Basket!=null && c.TextWindow==null && c.Basket.ActiveType==2,"旧文字入口重定向到统一篮子的文字筛选");c.Basket.Close();
c.ShowBasket();await Task.Delay(120);
Render((FrameworkElement)c.Basket.Content,"basket.png");
c.Shelf.Reveal(false,false,0);Check(c.Basket==null && !c.Shelf.ChoosingBasket,"绳子收起时篮子弹出层同步关闭");c.Shelf.Reveal(true,true,0);
c.ShowTexts();await Task.Delay(150);Check(Find(c.Basket,"粘贴") && Find(c.Basket,"挂上绳子") ,"统一篮子提供粘贴和手动挂绳");Render((FrameworkElement)c.Basket.Content,"texts.png");c.Basket.Close();
var original=c.Gallery.Collected[0];c.Gallery.Split(original,c.Texts);Check(c.Texts.Items.Count==2 && c.Gallery.Hanging.Count==3,"组合卡片拆分后两个内容均保留");
var note=c.Texts.Items.First(t=>t.Content==combined.Text);c.Gallery.AttachText(original,note,false,false);Check(c.Texts.Items.Count==1 && c.Gallery.Note(original)==combined.Text,"已有文字重新关联图片");
var restored=new StateStore(root);Check(restored.State.Texts.Count==1 && restored.State.ImageNotes.Count==1 && restored.State.Texts[0].Hanging,"组合和文字挂绳状态持久保存");
c.Texts.AutoHang=false;await c.AcceptCard(new ClipboardCard {Text="先留在信封"});Check(!c.Texts.Items[0].Hanging,"手动进入的文字遵循文字自动上绳开关");
c.Captures.AutoHang=false;combined.Text="先留在篮子";await c.AcceptCard(combined);Check(c.Gallery.Collected.Count==2 && c.Gallery.Items.Count==1,"带新备注的图片遵循图片自动上绳开关");
int count=c.Gallery.Collected.Count;var ignored=await c.Captures.ImportClipboardCard(combined,delegate{return false;});Check(ignored==null && c.Gallery.Collected.Count==count,"自动获取取消后不落入新记录");
combined.Text="保存必须整体回滚";count=c.Gallery.Collected.Count;var files=Directory.GetFiles(store.Inbox).Length;
using(var locked=new FileStream(Path.Combine(root,"state.json"),FileMode.Open,FileAccess.Read,FileShare.None)){bool failed=false;try{await c.AcceptCard(combined);}catch(IOException){failed=true;}Check(failed && c.Gallery.Collected.Count==count && Directory.GetFiles(store.Inbox).Length==files,"保存失败不能留下裸图记录或孤立文件");}
c.Gallery.RemovePicture(original,c.Texts);
var gallery2=new Gallery(new StateStore(root));using(var pipeline2=new CapturePipeline(gallery2,app.Dispatcher,Path.Combine(root,"missing"))){await pipeline2.Start();Check(!gallery2.Collected.Any(i=>i.Path==original.Path) && File.Exists(original.Path),"仅移除图片后重启不复活，原文件仍保留");}
var deletion=c.Gallery.Collected.First();
bool confirmation=false;
var guard=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
guard.Tick+=delegate{var dialog=FindWindow(null,"删除内容卡片");if(dialog!=IntPtr.Zero){confirmation=true;SendMessage(dialog,0x0111,new IntPtr(7),IntPtr.Zero);}};
guard.Start();try{c.Act(ShotAction.Trash,deletion);}finally{guard.Stop();}
Check(!confirmation && !File.Exists(deletion.Path) && !c.Gallery.Collected.Contains(deletion) && !new StateStore(root).State.ImageNotes.ContainsKey(deletion.Path),"图文组合删除不弹确认框，图片永久删除并清除备注");
int beforeBatch = c.Gallery.Collected.Count;
var batch = new ClipboardCard { Picture = combined.Picture, AdditionalPictures = new[] { combined.Picture }, Text = "同批文字" };
var pastedGroup = await c.Captures.ImportClipboardCard(batch);
var persisted = new StateStore(root).State;
Check(c.Gallery.Collected.Count == beforeBatch + 1 && c.Gallery.PicturePaths(pastedGroup).Length == 2 &&
    persisted.AttachedImages[pastedGroup.Path].Count == 1 && c.Gallery.Note(pastedGroup) == batch.Text,
    "一次粘贴多图和文字只创建一条备忘");
Console.WriteLine("卡片集成测试："+passed+" 通过，0 失败");
}catch(Exception error){Console.WriteLine("失败："+error);Environment.ExitCode=1;}finally{if(c!=null)c.Dispose();app.Shutdown(Environment.ExitCode);}};
app.Run();
}
static bool Find(DependencyObject node,string label){var text=node as TextBlock;if(text!=null && text.Text==label)return true;var content=node as ContentControl;if(content!=null && content.Content as string==label)return true;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find(VisualTreeHelper.GetChild(node,i),label))return true;return false;}
static DependencyObject FindControl(DependencyObject node, Func<DependencyObject, bool> match) {
    if (match(node)) return node;
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) {
        var result = FindControl(VisualTreeHelper.GetChild(node, i), match);
        if (result != null) return result;
    }
    return null;
}
static void Render(FrameworkElement element,string name){element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"card-previews");Directory.CreateDirectory(directory);using(var file=File.Create(Path.Combine(directory,name)))encoder.Save(file);}
}
}
