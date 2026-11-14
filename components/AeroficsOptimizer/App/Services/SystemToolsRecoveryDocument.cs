using System;
using System.Collections.Generic;

namespace SystemOptimizer.App.Services;

internal sealed record SystemToolsRecoveryDocument(int SchemaVersion, PowerPlanRecoveryEntry? PowerPlan, IReadOnlyList<StartupItemRecoveryEntry> StartupItems, IReadOnlyList<SystemServiceRecoveryEntry> Services)
{
	internal static SystemToolsRecoveryDocument Empty { get; } = new SystemToolsRecoveryDocument(1, null, Array.Empty<StartupItemRecoveryEntry>(), Array.Empty<SystemServiceRecoveryEntry>());
}
