using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AeroficsLauncher;

internal static class CpuSetTopologyReader
{
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetSystemCpuSetInformation(nint information, uint length, out uint returnedLength, nint process, uint flags);

	public static CpuSetTopology Read()
	{
		GetSystemCpuSetInformation(IntPtr.Zero, 0u, out var returnedLength, IntPtr.Zero, 0u);
		if (returnedLength == 0)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows CPU Set 拓扑");
		}
		nint num = Marshal.AllocHGlobal((int)returnedLength);
		try
		{
			if (!GetSystemCpuSetInformation(num, returnedLength, out var returnedLength2, IntPtr.Zero, 0u))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			List<CpuSetDescriptor> list = new List<CpuSetDescriptor>();
			uint num2;
			for (int i = 0; i + 8 <= returnedLength2; i += checked((int)num2))
			{
				num2 = (uint)Marshal.ReadInt32(num, i);
				int num3 = Marshal.ReadInt32(num, i + 4);
				if (num2 < 32 || i + num2 > returnedLength2)
				{
					throw new InvalidDataException("CPU Set 数据格式无效");
				}
				if (num3 == 0)
				{
					list.Add(new CpuSetDescriptor
					{
						Id = (uint)Marshal.ReadInt32(num, i + 8),
						Group = (ushort)Marshal.ReadInt16(num, i + 12),
						LogicalProcessorIndex = Marshal.ReadByte(num, i + 14),
						CoreIndex = Marshal.ReadByte(num, i + 15),
						LastLevelCacheIndex = Marshal.ReadByte(num, i + 16),
						NumaNodeIndex = Marshal.ReadByte(num, i + 17),
						EfficiencyClass = Marshal.ReadByte(num, i + 18),
						Flags = Marshal.ReadByte(num, i + 19)
					});
				}
			}
            IReadOnlyList<CpuSetDescriptor>[] ccds = list.GroupBy(c => (c.Group, c.LastLevelCacheIndex))
                .OrderBy(g => g.Key.Group).ThenBy(g => g.Key.LastLevelCacheIndex)
                .Select(g => (IReadOnlyList<CpuSetDescriptor>)g.OrderBy(c => c.CoreIndex).ThenBy(c => c.LogicalProcessorIndex).ToArray()).ToArray();
			string fingerprint = string.Join(";", from s in list
				orderby s.Id
				select $"{s.Id}:{s.Group}:{s.LogicalProcessorIndex}:{s.CoreIndex}:{s.LastLevelCacheIndex}:{s.Flags & 0xE}");
			return new CpuSetTopology
			{
				CpuSets = list,
				Ccds = ccds,
				Fingerprint = fingerprint
			};
		}
		finally
		{
			Marshal.FreeHGlobal(num);
		}
	}
}
