using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroficsLauncher;

public sealed class CpuSetTopology
{
	public IReadOnlyList<CpuSetDescriptor> CpuSets { get; init; } = Array.Empty<CpuSetDescriptor>();

	public IReadOnlyList<IReadOnlyList<CpuSetDescriptor>> Ccds { get; init; } = Array.Empty<IReadOnlyList<CpuSetDescriptor>>();

	public string Fingerprint { get; init; } = "";

	public bool IsReliable
	{
		get
		{
			if (Ccds.Count > 0)
			{
				return Ccds.All((IReadOnlyList<CpuSetDescriptor> c) => c.Count > 0);
			}
			return false;
		}
	}
}
