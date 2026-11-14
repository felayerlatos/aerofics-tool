using System;
using System.IO;

namespace SystemOptimizer.App.Presentation;

internal sealed class PowerPlanImportRowViewModel
{
	public string FilePath { get; }

	public string Name { get; }

	public string Source => "本机文件";

	public PowerPlanImportRowViewModel(string filePath)
	{
		FilePath = filePath;
		Name = Path.GetFileNameWithoutExtension(filePath);
	}
}
