using System.Text.Json.Serialization;
namespace SystemOptimizer.App.Services;
[JsonSerializable(typeof(SystemToolsRecoveryDocument))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SystemToolsRecoveryJsonContext : JsonSerializerContext { }
