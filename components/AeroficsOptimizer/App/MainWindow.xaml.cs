using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using SystemOptimizer.App.Presentation;

namespace SystemOptimizer.App;
public partial class MainWindow : Window
{
    internal SystemToolsViewModel Model { get; } = new();
    public MainWindow()
    {
        InitializeComponent();
        DataContext = Model;
        AccessStatus.Text = App.IsReadOnlySession ? "只读预览 · 可以检查各页和当前状态" : App.IsAdministrator ? "管理员权限 · 设置仅在点击应用后更改 · 恢复记录保存在本机" : "当前可以查看系统状态；应用或恢复设置前，请以管理员身份重新打开。";
        ElevateButton.Visibility = App.IsAdministrator || App.IsReadOnlySession ? Visibility.Collapsed : Visibility.Visible;
    }
    internal Task InitializeAsync() => Model.RefreshSummaryAsync();
    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { MessageBox.Show("未能重新打开：" + ex.Message, "系统优化"); }
    }
}
