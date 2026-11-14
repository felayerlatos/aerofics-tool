namespace SystemOptimizer.App.Configuration;

internal sealed record NvidiaProfilePresetOption(string Name, string Description, string NipXml, int SettingCount)
{
	public string SettingCountLabel => $"{SettingCount} 项原始 NIP 设置";
}
