using System;

namespace SystemOptimizer.App.Configuration;

internal sealed record ServiceNameRule(string Name, bool MatchPerUserSuffix = false)
{
	internal bool Matches(string serviceName)
	{
		if (!string.Equals(serviceName, Name, StringComparison.OrdinalIgnoreCase))
		{
			if (MatchPerUserSuffix)
			{
				return serviceName.StartsWith(Name + "_", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
		return true;
	}
}
