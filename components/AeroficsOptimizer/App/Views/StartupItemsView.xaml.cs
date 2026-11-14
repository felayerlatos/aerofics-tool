using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using SystemOptimizer.App.Presentation;

namespace SystemOptimizer.App.Views;

public partial class StartupItemsView : UserControl, IComponentConnector
{
	private DateTimeOffset _lastAutomaticRefresh;

	public StartupItemsView()
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
			if (systemToolsViewModel.RefreshStartupItemsCommand.CanExecute(null))
			{
				systemToolsViewModel.RefreshStartupItemsCommand.Execute(null);
			}
		}
	}
}
