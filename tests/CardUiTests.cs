using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
namespace ScreenLine.Tests {
internal static class CardUiTests {
[STAThread] public static void Main() {
new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
var text=new TextItem {Id="ui",Content="这是完整的文字\n第二行",Hanging=true};
var card=new ShotCard(new CaptureItem(text),delegate{});
if(card.Surface.Child==null || card.Grip.IsEnabled) throw new Exception("文字卡片应可见，且不可调用图片贴图");
if(!FindText(card.Surface,text.Content)) throw new Exception("文字必须以真实文字显示");
if(AutomationProperties.GetName(card.ActionMenu.Actions[ShotAction.Remove])!="取下放信封" || AutomationProperties.GetName(card.ActionMenu.Actions[ShotAction.Trash])!="删除文字")throw new Exception("文字菜单不能误称篮子或回收站");
card.Dispose(); Console.WriteLine("卡片界面测试：3 通过，0 失败");
Application.Current.Shutdown();
}
static bool FindText(DependencyObject node,string value) {var text=node as TextBlock; if(text!=null && text.Text==value)return true; for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(FindText(VisualTreeHelper.GetChild(node,i),value))return true; return false;}
}}
