using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenLine {
    internal sealed class ArchiveChoice : StackPanel, IDisposable {
        readonly StateStore store;
        string key;
        bool updating;
        internal readonly CheckBox Toggle = new CheckBox { Template = LightTheme.AutoHangSwitchTemplate, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        internal ArchiveChoice(StateStore store, Action<string> feedback) {
            this.store = store;
            Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new TextBlock { Text = "删除时归档", FontSize = 12, Foreground = LightTheme.Brush("#65717F"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) });
            Children.Add(Toggle); ToolTip = "仅影响当前备忘；未单独设置时跟随托盘开关";
            AutomationProperties.SetName(Toggle, "当前备忘删除时归档");
            RoutedEventHandler change = delegate {
                if (updating || key == null) return;
                try { store.SetCardArchive(key, Toggle.IsChecked == true); }
                catch (Exception error) { store.Log(error); Refresh(); feedback("归档设置保存失败，请重试。"); }
            };
            Toggle.Checked += change; Toggle.Unchecked += change;
            store.ArchiveSettingsChanged += Refresh; Refresh();
        }
        internal void SetKey(string value) { key = value; Refresh(); }
        void Refresh() { updating = true; Toggle.IsEnabled = key != null; Toggle.IsChecked = store.ShouldArchive(key); updating = false; }
        public void Dispose() { store.ArchiveSettingsChanged -= Refresh; }
    }
}
