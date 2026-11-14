using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using SystemOptimizer.App.Presentation;

namespace SystemOptimizer.App.Views;

public partial class ServicesOptimizationView : UserControl, IComponentConnector
{
	private DateTimeOffset _lastAutomaticRefresh;

	public ServicesOptimizationView()
	{
		InitializeComponent();
		IsVisibleChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			RefreshWhenShown();
		};
	}

	private void RefreshWhenShown()
	{
		if (IsVisible && !(DateTimeOffset.UtcNow - _lastAutomaticRefresh < TimeSpan.FromSeconds(2L)) && DataContext is SystemToolsViewModel systemToolsViewModel)
		{
			_lastAutomaticRefresh = DateTimeOffset.UtcNow;
			if (systemToolsViewModel.RefreshServicesCommand.CanExecute(null))
			{
				systemToolsViewModel.RefreshServicesCommand.Execute(null);
			}
		}
	}
}
