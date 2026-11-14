using System;
using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed class PowerPlanRowViewModel
{
	public Guid Id { get; }

	public string Name { get; }

	public bool IsActive { get; }

	public string State
	{
		get
		{
			if (!IsActive)
			{
				return "可用";
			}
			return "当前使用";
		}
	}

	public PowerPlanRowViewModel(PowerPlanDescriptor model)
	{
		Id = model.Id;
		Name = model.Name;
		IsActive = model.IsActive;
	}
}
