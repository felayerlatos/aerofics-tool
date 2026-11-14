using System.Collections.Generic;

namespace SystemOptimizer.App.Presentation;

internal sealed class SystemOptimizationGroupViewModel
{
	public string Title { get; }

	public IReadOnlyList<SystemOptimizationRowViewModel> Items { get; }

	public SystemOptimizationGroupViewModel(string title, IReadOnlyList<SystemOptimizationRowViewModel> items)
	{
		Title = title;
		Items = items;
	}
}
