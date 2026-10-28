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
static string root; static int passed;
static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("通过："+name);}
[STAThread] public static void Main(){
var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};LightTheme.Install(app);
app.Startup+=async delegate {DesktopController c=null; try {
root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","cards-ui-"+Guid.NewGuid().ToString("N"));
var store=new StateStore(root);c=new DesktopController(store,Path.Combine(root,"screenshots"),false);await c.Start();
Check(!store.State.AutoClipboard && c.Shelf.BasketButton.IsVisible && c.Shelf.TextButton.IsVisible,"自动获取关闭时仍可进入篮子和信封手动粘贴");
c.TrayMenu.Pictures.IsChecked=true;Check(store.State.AutoClipboard && new StateStore(root).State.AutoClipboard,"统一自动获取开关持久保存");c.TrayMenu.Pictures.IsChecked=false;
await c.AcceptCard(new ClipboardCard {Text="可以挂绳的文字\n保留完整换行与缩进"});
Check(c.Texts.Items.Count==1 && c.Gallery.Hanging.Count==1 && c.Shelf.CardFor("text:"+c.Texts.Items[0].Id)!=null,"文字进入信封并显示在绳子");
var pixels=new byte[240*140*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=170;pixels[i+1]=110;pixels[i+2]=40;pixels[i+3]=255;}
var image=BitmapSource.Create(240,140,96,96,PixelFormats.Bgra32,null,pixels,960);
var combined=new ClipboardCard {Text="图片下方的备注摘要：完整备注仍然保留。",Picture=new ClipboardPicture{Bytes=ImageContent.EncodeClipboard(image),Extension=".png"}};
await c.AcceptCard(combined);Check(c.Gallery.Collected.Count==1 && c.Texts.Items.Count==1 && c.Gallery.Hanging.Count==2,"一次图文只创建一张组合卡片");
c.Shelf.Update();c.Shelf.Reveal(true,true,0); await Task.Delay(180);
Render((FrameworkElement)c.Shelf.Content,"rope.png");
var editor=new CardEditorWindow(c.Gallery,c.Gallery.Collected[0],false){Owner=c.Shelf};editor.Show();await Task.Delay(100);Render((FrameworkElement)editor.Content,"editor.png");editor.Close();
c.ShowBasket();await Task.Delay(180);Check(Find(c.Basket,"粘贴图片") && Find(c.Basket,"关联 / 编辑图文…"),"篮子提供粘贴和关联入口");Render((FrameworkElement)c.Basket.Content,"basket.png");c.Basket.Close();
c.ShowTexts();await Task.Delay(150);Check(Find(c.TextWindow,"粘贴文字") && Find(c.TextWindow,"挂上绳子") && Find(c.TextWindow,"新文字自动上绳"),"信封提供粘贴、自动上绳和手动挂绳");Render((FrameworkElement)c.TextWindow.Content,"texts.png");c.TextWindow.Close();
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
Console.WriteLine("卡片集成测试："+passed+" 通过，0 失败");
}catch(Exception error){Console.WriteLine("失败："+error);Environment.ExitCode=1;}finally{if(c!=null)c.Dispose();app.Shutdown(Environment.ExitCode);}};
app.Run();
}
static bool Find(DependencyObject node,string label){var content=node as ContentControl;if(content!=null && content.Content as string==label)return true;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find(VisualTreeHelper.GetChild(node,i),label))return true;return false;}
static void Render(FrameworkElement element,string name){element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"card-previews");Directory.CreateDirectory(directory);using(var file=File.Create(Path.Combine(directory,name)))encoder.Save(file);}
}
}
