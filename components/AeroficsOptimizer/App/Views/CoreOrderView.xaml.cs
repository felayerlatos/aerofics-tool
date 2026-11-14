using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SystemOptimizer.App.Presentation;

namespace SystemOptimizer.App.Views;
public sealed class RankConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int number ? (number + 1).ToString("00") : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public partial class CoreOrderView : UserControl
{
    public CoreOrderView() { InitializeComponent(); }
    private void Move_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((Button)sender).Tag).Split(':');
        var model = (SchedulerViewModel)DataContext;
        var list = parts[0] == "game" ? Games : Backgrounds;
        var order = parts[0] == "game" ? model.GameOrder : model.BackgroundOrder;
        var item = list.SelectedItem as CoreChoice;
        int delta = parts[1] switch { "up" => -1, "down" => 1, "top" => -order.Count, _ => order.Count };
        model.Move(order, item, delta);
        if (item != null) { list.SelectedItem = item; list.ScrollIntoView(item); }
    }
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((Button)sender).Tag).Split(':');
        var model = (SchedulerViewModel)DataContext;
        model.Preset(parts[0] == "game" ? model.GameOrder : model.BackgroundOrder, parts[1]);
    }
}
