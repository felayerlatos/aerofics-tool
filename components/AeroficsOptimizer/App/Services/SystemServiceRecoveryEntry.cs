using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Services;

internal sealed record SystemServiceRecoveryEntry(SystemServiceConfigurationSnapshot Original, SystemServiceStartupType ManagedStartupType, bool MutationPending);
