using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AeroficsLauncher;

internal static class RunningProcessCatalog
{
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

	internal static List<RunningProcessEntry> Read()
	{
		List<RunningProcessEntry> list = new List<RunningProcessEntry>();
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			using (process)
			{
				try
				{
					int id = process.Id;
					string name = process.ProcessName + ".exe";
					string title = "";
					string path = "";
					string access = "可添加";
					try
					{
						title = process.MainWindowTitle;
					}
					catch (InvalidOperationException)
					{
					}
					using SafeProcessHandle safeProcessHandle = OpenProcess(4096u, inherit: false, id);
					StringBuilder stringBuilder = new StringBuilder(32768);
					uint size = (uint)stringBuilder.Capacity;
					if (!safeProcessHandle.IsInvalid && QueryFullProcessImageName(safeProcessHandle, 0u, stringBuilder, ref size))
					{
						path = stringBuilder.ToString();
					}
					else
					{
						access = "无法读取路径 / 已退出";
					}
					list.Add(new RunningProcessEntry(id, name, title, path, access));
				}
				catch (Exception ex2) when (((ex2 is Win32Exception || ex2 is InvalidOperationException || ex2 is NotSupportedException) ? 1 : 0) != 0)
				{
				}
			}
		}
		return list.OrderBy<RunningProcessEntry, string>((RunningProcessEntry e) => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy((RunningProcessEntry e) => e.Pid).ToList();
	}

	internal static bool Matches(RunningProcessEntry entry, string query)
	{
		return new string[4]
		{
			entry.Name,
			entry.Title,
			entry.Path,
			entry.Pid.ToString()
		}.Any((string value) => value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
	}

	internal static string[] Merge(IEnumerable<string> existing, IEnumerable<string> additions)
	{
		return (from s in existing.Concat(additions)
			select s.Trim() into s
			where s.Length > 0
			select s).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}
}
