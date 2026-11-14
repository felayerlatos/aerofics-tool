using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using SystemOptimizer.App.Presentation;

namespace SystemOptimizer.App.Views;

public partial class PowerPlansView : UserControl, IComponentConnector
{
	private DateTimeOffset _lastAutomaticRefresh;

	public PowerPlansView()
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
			if (systemToolsViewModel.RefreshPowerPlansCommand.CanExecute(null))
			{
				systemToolsViewModel.RefreshPowerPlansCommand.Execute(null);
			}
		}
	}
}
