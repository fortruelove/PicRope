using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenLine
{
    internal sealed class ImageMenu : ContextMenu
    {
        static readonly Style actionStyle = (Style)XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='Height' Value='32'/><Setter Property='Padding' Value='10,0'/>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='7' BorderBrush='Transparent' BorderThickness='1'>
      <ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F0F3F7'/></Trigger>
      <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Surface' Property='Background' Value='#FFF0ED'/></MultiTrigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#6E97CB'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");

        readonly TextBlock details;
        internal static readonly string[] CardPalette = { "#F3EBD5", "#E3EDDF", "#E1EBF3", "#EBE4F1", "#F2E2E1", "#EBC6C3" };
        internal static readonly string[] CardColorNames = { "米黄", "浅绿", "雾蓝", "淡紫", "柔粉", "浅红" };
        internal Action<int> ChooseColor;
        internal readonly Button[] ColorButtons = new Button[CardPalette.Length];
        internal readonly Dictionary<ShotAction, Button> Actions = new Dictionary<ShotAction, Button>();

        internal ImageMenu(Action<ShotAction> execute)
        {
            Width = 220; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Foreground = LightTheme.Brush("#283343"); HasDropShadow = false; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            var panel = new StackPanel();
            KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
            var heading = new Grid { Margin = new Thickness(10, 5, 10, 7) };
            heading.Children.Add(new TextBlock { Text = "图片", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#536172") });
            details = new TextBlock { FontSize = 11, Foreground = LightTheme.Brush("#65717F"), HorizontalAlignment = HorizontalAlignment.Right };
            heading.Children.Add(details); panel.Children.Add(heading);
            panel.Children.Add(new TextBlock { Text = "卡片背景色", Margin = new Thickness(10, 2, 10, 4), FontSize = 11 });
            var colors = new Grid { Margin = new Thickness(10, 0, 10, 6) };
            for (int n = 0; n < 3; n++) colors.ColumnDefinitions.Add(new ColumnDefinition());
            for (int n = 0; n < 2; n++) colors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int n = 0; n < CardPalette.Length; n++) {
                int color = n;
                var button = new Button { Content = CardColorNames[n], MinWidth = 0, Height = 28, Margin = new Thickness(0, 0, n % 3 == 2 ? 0 : 4, 4), Background = LightTheme.Brush(CardPalette[n]), Foreground = LightTheme.Brush("#394452"), BorderBrush = LightTheme.Brush("#CDD3DB"), BorderThickness = new Thickness(1), ToolTip = "背景色：" + CardColorNames[n] };
                button.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='5'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
                AutomationProperties.SetName(button, "卡片背景色 " + CardColorNames[n]);
                Grid.SetColumn(button, n % 3); Grid.SetRow(button, n / 3);
                button.Click += delegate { IsOpen = false; if (ChooseColor != null) ChooseColor(color); };
                ColorButtons[n] = button; colors.Children.Add(button);
            }
            panel.Children.Add(colors);
            panel.Children.Add(Action("打开", "open", ShotAction.Open, execute));
            panel.Children.Add(Action("编辑", "edit", ShotAction.Edit, execute));
            panel.Children.Add(Action("保存副本…", "save", ShotAction.Save, execute));
            panel.Children.Add(Action("打开位置", "folder", ShotAction.Locate, execute));
            panel.Children.Add(Action("关联 / 编辑图文…", "edit", ShotAction.CardEdit, execute));
            panel.Children.Add(Action("复制文字", "copy", ShotAction.CopyText, execute));
            panel.Children.Add(Action("复制图片和文字", "copy", ShotAction.CopyBoth, execute));
            panel.Children.Add(Action("拆分为图片和文字", "folder", ShotAction.Split, execute));
            panel.Children.Add(Action("仅移除备注", "trash", ShotAction.RemoveNote, execute));
            panel.Children.Add(Action("仅移除图片（保留原文件）", "trash", ShotAction.RemovePicture, execute));
            panel.Children.Add(new Border { Height = 1, Background = LightTheme.Brush("#E8EBEF"), Margin = new Thickness(6, 4, 6, 4) });
            panel.Children.Add(Action("取下放篮子", "folder", ShotAction.Remove, execute));
            panel.Children.Add(Action("删除图片", "trash", ShotAction.Trash, execute));
            var surface = LightTheme.Card(panel); surface.Padding = new Thickness(4);
            Items.Add(surface);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Items[0]") { RelativeSource = RelativeSource.TemplatedParent });
            Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = content };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { IsOpen = false; args.Handled = true; }
                else if (args.OriginalSource == this && (args.Key == Key.Down || args.Key == Key.Up || args.Key == Key.Tab))
                {
                    bool previous = args.Key == Key.Up || (args.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    Actions[previous ? ShotAction.Trash : ShotAction.Open].Focus(); args.Handled = true;
                }
                else if (args.Key == Key.Space)
                {
                    var button = args.OriginalSource as Button;
                    if (button == null) return;
                    if (!args.IsRepeat) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    args.Handled = true;
                }
            };
            Opened += delegate { Focus(); };
        }

        internal void SetCard(bool text, bool note) {
            SetLabel(ShotAction.Remove, "取下放篮子");
            SetLabel(ShotAction.Trash, text ? "删除文字" : note ? "删除图片和备注" : "删除图片");
            SetLabel(ShotAction.Edit, text ? "编辑文字 / 添加配图…" : "编辑图片");
            foreach (var action in new[] { ShotAction.Save, ShotAction.Locate, ShotAction.CopyBoth, ShotAction.Split, ShotAction.RemoveNote, ShotAction.RemovePicture })
                Actions[action].Visibility = text || (!note && action != ShotAction.Save && action != ShotAction.Locate) ? Visibility.Collapsed : Visibility.Visible;
            Actions[ShotAction.CopyText].Visibility = text || note ? Visibility.Visible : Visibility.Collapsed;
        }
        void SetLabel(ShotAction action, string value) {
            var button=Actions[action]; var panel=(StackPanel)button.Content;
            ((TextBlock)panel.Children[1]).Text=value; AutomationProperties.SetName(button,value);
        }
        internal void SetDetails(string value) { details.Text = value; }

        Button Action(string title, string icon, ShotAction action, Action<ShotAction> execute)
        {
            var color = LightTheme.Brush(action == ShotAction.Trash ? "#BB514E" : "#283343");
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Viewbox { Width = 18, Height = 18, Child = new Path { Data = LightMenu.IconGeometry(icon), Stroke = color, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } });
            label.Children.Add(new TextBlock { Text = title, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button {
                Content = label, Style = actionStyle, Tag = action == ShotAction.Trash ? "danger" : null,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(button, title);
            button.Click += delegate { IsOpen = false; execute(action); };
            Actions.Add(action, button); return button;
        }

    }
}
