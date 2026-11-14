using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using SystemOptimizer.App.Services;
using SystemOptimizer.App.Configuration;
using SystemOptimizer.Windows.SystemTools;

namespace SystemOptimizer.App;
internal static class Verification
{
    internal static async Task<int> RunAsync(string destination)
    {
        var results = new Dictionary<string, object>();
        var blocked = new List<string>();
        bool passed = false;
        try
        {
            var window = new ShellWindow();
            await window.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(90));
            var model = window.SystemModel;
            await model.RefreshStartupItemsAsync();
            await model.RefreshServicesAsync();
            var items = model.OptimizationGroups.SelectMany(g => g.Items).ToArray();
            if (items.Length != 21)
            {
                // Limited execution environments may deny registry reads; retain that result and still check the UI.
                try
                {
                    await new WindowsSystemOptimizationService().EnumerateAsync();
                    throw new Exception($"Expected 21 optimizations, found {items.Length}: {model.SystemOptimizationStatus}");
                }
                catch (System.Security.SecurityException ex) { blocked.Add("System optimization inventory: " + ex.Message); }
                catch (UnauthorizedAccessException ex) { blocked.Add("System optimization inventory: " + ex.Message); }
            }
            var presets = NvidiaProfilePresetCatalog.Load();
            if (presets.Count != 3 || presets.Any(p => p.Name.Contains("fox", StringComparison.OrdinalIgnoreCase))) throw new Exception("Preset selection mismatch");
            var power = await new WindowsPowerPlanService().EnumerateAsync();
            var startup = await new WindowsStartupItemService().EnumerateAsync();
            var services = await new WindowsServiceConfigurationService().EnumerateAsync();
            if (!power.IsSuccess) blocked.Add("Power inventory: " + power.Error?.Message);
            if (!startup.IsSuccess) blocked.Add("Startup inventory: " + startup.Error?.Message);
            if (!services.IsSuccess) blocked.Add("Service inventory: " + services.Error?.Message);
            results["optimizationCount"] = items.Length;
            results["expectedOptimizationCount"] = 21;
            results["optimizationReadStatus"] = model.SystemOptimizationStatus;
            results["optimizationGroups"] = model.OptimizationGroups.Select(g => g.Title).ToArray();
            results["presets"] = presets.Select(p => new { p.Name, p.SettingCount }).ToArray();
            results["powerPlanCount"] = power.Value?.Count ?? 0;
            results["startupItemCount"] = startup.Value?.Count ?? 0;
            results["serviceCount"] = services.Value?.Count ?? 0;
            results["serviceGroups"] = model.ServiceGroups.Count;
            results["vbsStatus"] = model.VbsStatus;
            results["win32Status"] = model.Win32PriorityStatus;
            results["nvidiaStatus"] = model.NvidiaPowerStateStatus;
            // Invalid XML is rejected before any driver access; this verifies the actual child-process protocol.
            var workerCheck = await new WindowsNvidiaProfilePresetService().ApplyNipAsync("Protocol validation", "<Profiles />");
            if(workerCheck.IsSuccess || workerCheck.Error?.Code != "nvidia_profile.base_profile_missing")
                throw new Exception("NVIDIA worker protocol check failed: " + workerCheck.Error?.Message);
            results["nvidiaWorkerProtocol"] = "passed (invalid input rejected before driver access)";
            var recoveryFile=Path.Combine(Path.GetDirectoryName(destination)!,"verification-recovery.json");
            var recovery=new SystemToolsRecoveryStore(recoveryFile);
            if(!recovery.Save(SystemToolsRecoveryDocument.Empty).IsSuccess || !recovery.Load().IsSuccess)
                throw new Exception("Recovery record round trip failed");
            File.Delete(recoveryFile);
            results["recoveryRecordRoundTrip"] = true;
            results["writesDisabled"] = !model.ApplyOptimizationCommand.CanExecute(null) && !model.ApplyNvidiaProfilePresetCommand.CanExecute(null);
            if (!(bool)results["writesDisabled"]) throw new Exception("Read-only mode failed");
            var root = (FrameworkElement)window.Content;
            window.Content = null;
            var surface = new Grid { Width=1440, Height=876, Background=window.Background, DataContext=window };
            surface.Children.Add(root);
            surface.Measure(new Size(1440,876));
            surface.Arrange(new Rect(0,0,1440,876));
            surface.UpdateLayout();
            var previews = Path.Combine(Path.GetDirectoryName(destination)!, "preview");
            Directory.CreateDirectory(previews);
            for(int index = 0; index < ShellWindow.PageKeys.Length; index++)
            {
                window.Navigate(ShellWindow.PageKeys[index]);
                surface.UpdateLayout();
                await root.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ContextIdle);
                foreach(var button in Descendants<Button>(root))
                    if(BindingOperations.IsDataBound(button,ButtonBase.CommandProperty) && button.Command == null)
                        throw new Exception("Unresolved button command: " + button.Content);
                var bitmap = new RenderTargetBitmap(1440,876,96,96,PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                // Only generic pages are retained; local startup and service details stay out of previews.
                if(index is 0 or 1 or 2 or 3 or 4 or 8)
                {
                    using var stream=File.Create(Path.Combine(previews,$"page-{index}.png"));
                    encoder.Save(stream);
                }
            }
            results["renderedPages"] = ShellWindow.PageKeys.Length;
            var scheduler = window.SchedulerModel;
            var baselineGame = scheduler.GameOrder.Select(c => c.Id).ToArray();
            var baselineBackground = scheduler.BackgroundOrder.Select(c => c.Id).ToArray();
            scheduler.Preset(scheduler.GameOrder, "reverse");
            var mapped = scheduler.ReadSettings();
            if (!mapped.GameCoreOrder.SequenceEqual(baselineGame.Reverse()) || !mapped.BackgroundCoreOrder.SequenceEqual(baselineBackground))
                throw new Exception("Independent UI core orders did not map to scheduler settings");
            results["coreOrderUiMapping"] = true;
            results["schedulerReadOnly"] = !scheduler.CanStart;
            if (scheduler.CanStart) throw new Exception("Preview scheduler can write");
            scheduler.Dispose();
            results["uiPassed"] = true;
            passed = blocked.Count == 0;
        }
        catch (Exception ex) { results["error"] = ex.GetType().Name + ": " + ex.Message; results["detail"] = ex.ToString(); }
        results["passed"] = passed;
        results["blockedChecks"] = blocked;
        results["readOnly"] = true;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return passed ? 0 : 1;
    }
    private static T? Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T result) return result;
        for(int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child=Find<T>(VisualTreeHelper.GetChild(root,i));
            if(child!=null) return child;
        }
        return null;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T result) yield return result;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
