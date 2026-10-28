using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine.Tests
{
    internal static class RopeSortTests
    {
        static int count;
        static void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("通过：" + label); }
        [STAThread] public static void Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LightTheme.Install(app);
            ShelfWindow shelf = null;
            try
            {
                var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "sort-" + Guid.NewGuid().ToString("N"));
                var store = new StateStore(root); var gallery = new Gallery(store); var texts = new TextCollection(store);
                var first = texts.Add("第一条文字"); var second = texts.Add("第二条文字");
                shelf = new ShelfWindow(gallery, delegate { }, delegate { }); texts.Changed += shelf.Update;
                shelf.Show(); shelf.Reveal(true, true, 60); shelf.UpdateLayout();
                var viewport = (ScrollViewer)Field(shelf, "viewport"); var content = (Canvas)Field(shelf, "content");
                var rope = (System.Windows.Shapes.Path)Field(shelf, "rope");
                var original = store.State.CardOrder.ToArray();
                var card = shelf.CardFor("text:" + second.Id);
                var payload = new DataObject(DataFormats.UnicodeText, second.Content);
                shelf.BeginSort(card, payload);
                Check(rope.Stroke == Brushes.DodgerBlue && rope.Effect != null && card.Grip.Effect != null, "开始拖动高亮整条绳子和对应夹子");
                Check(store.State.CardOrder.SequenceEqual(original), "拖动开始不提前改序");
                Raise(viewport, payload, new Point(-1, 50), DragDrop.PreviewDropEvent);
                Check(store.State.CardOrder.SequenceEqual(original), "绳外松开不提交排序");
                double left = (content.Width - 2 * 172) / 2;
                Raise(viewport, payload, new Point(left, 60), DragDrop.PreviewDragOverEvent);
                Check(store.State.CardOrder.SequenceEqual(original), "悬停位置预览不提交排序");
                Raise(viewport, payload, new Point(left, 60), DragDrop.PreviewDropEvent);
                Check(gallery.Hanging[0].Text == second && new StateStore(root).State.CardOrder[0] == card.Item.Path, "绳内松开才提交并保存排序");
                shelf.EndSort(card);
                Check(rope.Effect == null && card.Grip.Effect == null, "拖动结束清理绳子和夹子高亮");
                original = store.State.CardOrder.ToArray();
                Raise(viewport, payload, new Point(left, 60), DragDrop.PreviewDropEvent);
                Check(store.State.CardOrder.SequenceEqual(original), "过期拖拽不能修改顺序");
                shelf.BeginSort(card, payload); shelf.EndSort(card);
                Check(store.State.CardOrder.SequenceEqual(original), "取消拖动保持顺序");
                Check(!shelf.OnSortingRope(new Point(40, 221)) && !shelf.OnSortingRope(new Point(-1, 30)), "绳外边界拒绝排序");
                Check(payload.GetText() == second.Content, "排序保留 Unicode 文本拖出内容");
                var window = new TextWindow(texts, shelf);
                window.Search.Text = first.Content;
                window.SelectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.DeleteSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!texts.Items.Contains(first) && !gallery.Hanging.Any(i => i.Text == first), "文字批量删除一次点击完成并同步卡片"); window.Close();
                Check(AppUpdates.Repository == "https://github.com/fortruelove/PicRope" && AppUpdates.Endpoint == "https://api.github.com/repos/fortruelove/PicRope/releases/latest", "更新检查和下载白名单使用自己的仓库");
                Check(AppUpdates.CurrentVersion == "1.2.4", "发布版本为 1.2.4");
                Console.WriteLine("绳子排序测试：{0} 通过，0 失败", count);
            }
            catch (Exception error) { Console.WriteLine(error); Environment.ExitCode = 1; }
            finally { if (shelf != null) shelf.Close(); app.Shutdown(); }
        }
        static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        static void Raise(ScrollViewer target, DataObject data, Point point, RoutedEvent routedEvent)
        {
            var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            var e = (DragEventArgs)ctor.Invoke(new object[] { data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move, target, point });
            e.RoutedEvent = routedEvent; target.RaiseEvent(e);
        }
    }
}
