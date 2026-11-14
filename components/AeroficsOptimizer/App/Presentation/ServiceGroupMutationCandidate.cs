using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed record ServiceGroupMutationCandidate(string DisplayName, SystemServiceConfigurationSnapshot Before, SystemServiceConfigurationSnapshot Original);
