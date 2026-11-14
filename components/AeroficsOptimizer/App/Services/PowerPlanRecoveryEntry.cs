using System;
using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Services;

internal sealed record PowerPlanRecoveryEntry(PowerPlanSnapshot Original, Guid ManagedActivePlanId, bool MutationPending);
