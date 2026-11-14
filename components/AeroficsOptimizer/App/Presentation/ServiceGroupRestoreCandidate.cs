using SystemOptimizer.App.Services;
using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed record ServiceGroupRestoreCandidate(string DisplayName, SystemServiceConfigurationSnapshot Before, SystemServiceRecoveryEntry Recovery);
