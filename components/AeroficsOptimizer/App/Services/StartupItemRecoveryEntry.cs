using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Services;

internal sealed record StartupItemRecoveryEntry(StartupItemSnapshot Original, bool ManagedIsEnabled, bool MutationPending);
