using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SystemOptimizer.Core.Operations;
using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Services;

internal sealed class SystemToolsRecoveryStore
{
	private const int MaximumFileBytes = 1048576;

	private readonly object gate;

	private readonly string path;

	internal SystemToolsRecoveryStore()
		: this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemOptimizer", "Data", "Backups", "system-tools-recovery.json"))
	{
	}

	internal SystemToolsRecoveryStore(string path)
	{
		gate = new object();
		this.path = Path.GetFullPath(path ?? throw new ArgumentNullException("path"));
	}

	internal OperationResult<SystemToolsRecoveryDocument> Load()
	{
		lock (gate)
		{
			return LoadCore();
		}
	}

	internal OperationResult<SystemToolsRecoveryDocument> Save(SystemToolsRecoveryDocument document)
	{
		ArgumentNullException.ThrowIfNull(document, "document");
		lock (gate)
		{
			OperationResult<SystemToolsRecoveryDocument> operationResult = LoadCore();
			if (!operationResult.IsSuccess)
			{
				return operationResult;
			}
			string text = Validate(document);
			if (text != null)
			{
				return Invalid(text);
			}
			string text2 = string.Empty;
			try
			{
				string? directoryName = Path.GetDirectoryName(path);
				Directory.CreateDirectory(directoryName);
				text2 = Path.Combine(directoryName, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
				File.WriteAllText(text2, JsonSerializer.Serialize(document, SystemToolsRecoveryJsonContext.Default.SystemToolsRecoveryDocument));
				File.Move(text2, path, overwrite: true);
				return OperationResult<SystemToolsRecoveryDocument>.Success(document);
			}
			catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
			{
				return OperationResult<SystemToolsRecoveryDocument>.Failure("system_tools.recovery_write_failed", "无法保存系统工具恢复记录：" + ex.Message);
			}
			finally
			{
				if (!string.IsNullOrWhiteSpace(text2) && File.Exists(text2))
				{
					try
					{
						File.Delete(text2);
					}
					catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException) ? true : false)
					{
					}
				}
			}
		}
	}

	private OperationResult<SystemToolsRecoveryDocument> LoadCore()
	{
		try
		{
			if (!File.Exists(path))
			{
				return OperationResult<SystemToolsRecoveryDocument>.Success(SystemToolsRecoveryDocument.Empty);
			}
			long length = new FileInfo(path).Length;
			if (length <= 0 || length > 1048576)
			{
				return Invalid("系统工具恢复记录为空或超过大小限制，已保留原文件且不会覆盖。");
			}
			SystemToolsRecoveryDocument systemToolsRecoveryDocument = JsonSerializer.Deserialize(File.ReadAllText(path), SystemToolsRecoveryJsonContext.Default.SystemToolsRecoveryDocument);
			string text = Validate(systemToolsRecoveryDocument);
			return (text == null) ? OperationResult<SystemToolsRecoveryDocument>.Success(systemToolsRecoveryDocument) : Invalid(text);
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is NotSupportedException) ? true : false)
		{
			return Invalid("无法读取系统工具恢复记录，已保留原文件且不会覆盖：" + ex.Message);
		}
	}

	private static string? Validate(SystemToolsRecoveryDocument? document)
	{
		if ((object)document == null || document.SchemaVersion != 1 || document.StartupItems == null || document.Services == null || document.StartupItems.Count > 512 || document.Services.Count > 1024)
		{
			return "系统工具恢复记录的版本或条目数无效，已保留原文件且不会覆盖。";
		}
		PowerPlanRecoveryEntry powerPlan = document.PowerPlan;
		if ((object)powerPlan != null && (powerPlan.Original.ActivePlanId == Guid.Empty || powerPlan.ManagedActivePlanId == Guid.Empty || powerPlan.Original.CapturedAt == default(DateTimeOffset)))
		{
			return "电源计划恢复记录无效，已保留原文件且不会覆盖。";
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (StartupItemRecoveryEntry startupItem in document.StartupItems)
		{
			StartupItemDescriptor startupItemDescriptor = startupItem?.Original?.Item;
			bool flag = (object)startupItemDescriptor == null || startupItem.Original.CapturedAt == default(DateTimeOffset) || !hashSet.Add(startupItemDescriptor.Id);
			if (!flag)
			{
				int length = startupItemDescriptor.Id.Length;
				bool flag2 = ((length > 1024 || length == 0) ? true : false);
				flag = flag2;
			}
			bool flag3 = flag;
			if (!flag3)
			{
				int length = startupItemDescriptor.Name.Length;
				bool flag2 = ((length > 512 || length == 0) ? true : false);
				flag3 = flag2;
			}
			bool flag4 = flag3 || startupItemDescriptor.Command.Length > 32768;
			if (!flag4)
			{
				int length = startupItemDescriptor.Location.Length;
				bool flag2 = ((length > 512 || length == 0) ? true : false);
				flag4 = flag2;
			}
			if (!flag4 && startupItemDescriptor.Id.IndexOf('\0') < 0 && startupItemDescriptor.Name.IndexOf('\0') < 0 && startupItemDescriptor.Command.IndexOf('\0') < 0 && startupItemDescriptor.Location.IndexOf('\0') < 0)
			{
				StartupRegistryValueKind valueKind = startupItemDescriptor.ValueKind;
				if ((valueKind == StartupRegistryValueKind.String || valueKind == StartupRegistryValueKind.ExpandString) && IsKnownStartupLocation(startupItemDescriptor.Location))
				{
					continue;
				}
			}
			return "启动项恢复记录包含重复或越界字段，已保留原文件且不会覆盖。";
		}
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (SystemServiceRecoveryEntry service in document.Services)
		{
			SystemServiceConfigurationSnapshot systemServiceConfigurationSnapshot = service?.Original;
			if ((object)systemServiceConfigurationSnapshot == null || systemServiceConfigurationSnapshot.CapturedAt == default(DateTimeOffset) || string.IsNullOrWhiteSpace(systemServiceConfigurationSnapshot.Name) || systemServiceConfigurationSnapshot.Name.Length > 256 || systemServiceConfigurationSnapshot.Name.Any(char.IsControl) || systemServiceConfigurationSnapshot.Name.Contains('\\') || systemServiceConfigurationSnapshot.Name.Contains('/') || !hashSet2.Add(systemServiceConfigurationSnapshot.Name) || !IsManagedServiceStartupType(systemServiceConfigurationSnapshot.StartupType) || !IsManagedServiceStartupType(service.ManagedStartupType))
			{
				return "服务恢复记录包含重复或越界字段，已保留原文件且不会覆盖。";
			}
		}
		return null;
	}

	private static bool IsKnownStartupLocation(string location)
	{
		if (!string.Equals(location, "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", StringComparison.Ordinal) && !string.Equals(location, "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run (64 位)", StringComparison.Ordinal))
		{
			return string.Equals(location, "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run (32 位)", StringComparison.Ordinal);
		}
		return true;
	}

	private static bool IsManagedServiceStartupType(SystemServiceStartupType startupType)
	{
		if ((uint)(startupType - 2) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static OperationResult<SystemToolsRecoveryDocument> Invalid(string message)
	{
		return OperationResult<SystemToolsRecoveryDocument>.Failure("system_tools.recovery_invalid", message);
	}
}
