namespace SystemOptimizer.App.Configuration;

internal sealed record Win32PriorityPresetOption(string Id, string Name, string ValueLabel, string BestFor, string Effect, string Impact, int? Value)
{
	public bool IsCustom => !Value.HasValue;
}
