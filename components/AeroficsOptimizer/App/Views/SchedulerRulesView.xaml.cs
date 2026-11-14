using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AeroficsLauncher;
using Microsoft.Win32;
using SystemOptimizer.App.Presentation;

namespace SystemOptimizer.App.Views;
public partial class SchedulerRulesView : UserControl
{
    public SchedulerRulesView() { InitializeComponent(); }
    private SchedulerViewModel Model => (SchedulerViewModel)DataContext;
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe", Multiselect = true };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) Model.AddPrograms((string)((Button)sender).Tag, picker.FileNames);
    }
    private async void Pick_Click(object sender, RoutedEventArgs e)
    {
        var target = (string)((Button)sender).Tag;
        var list = await Task.Run(RunningProcessCatalog.Read);
        var window = new Window { Owner = Window.GetWindow(this), Title = "添加正在运行的程序", Width = 900, Height = 590,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Style = (Style)FindResource(typeof(Window)) };
        var layout = new Grid { Margin = new Thickness(22) };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var search = new TextBox { Margin = new Thickness(0,0,0,14), ToolTip = "搜索进程名称、窗口标题或路径" };
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, ItemsSource = list };
        grid.Columns.Add(new DataGridTextColumn { Header = "程序", Binding = new Binding("Name"), Width = 170 });
        grid.Columns.Add(new DataGridTextColumn { Header = "窗口标题", Binding = new Binding("Title"), Width = 200 });
        grid.Columns.Add(new DataGridTextColumn { Header = "路径", Binding = new Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        search.TextChanged += (_, _) => grid.ItemsSource = list.Where(p => RunningProcessCatalog.Matches(p, search.Text)).ToArray();
        var add = new Button { Content = "添加所选程序", Style = (Style)FindResource("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        add.Click += (_, _) => { Model.AddPrograms(target, grid.SelectedItems.Cast<RunningProcessEntry>().Select(p => string.IsNullOrWhiteSpace(p.Path) ? p.Name : p.Path)); window.Close(); };
        Grid.SetRow(grid, 1); Grid.SetRow(add, 2); layout.Children.Add(search); layout.Children.Add(grid); layout.Children.Add(add); window.Content = layout; window.ShowDialog();
    }
}
