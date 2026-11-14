using System.Collections.Generic;
using System.Linq;

namespace SystemOptimizer.App.Configuration;

internal sealed record ServiceGroupDefinition(string Id, string Title, string Description, string DisabledImpact, string RiskLabel, IReadOnlyList<ServiceNameRule> ServiceRules)
{
	internal bool Matches(string serviceName)
	{
		return ServiceRules.Any((ServiceNameRule rule) => rule.Matches(serviceName));
	}
}
