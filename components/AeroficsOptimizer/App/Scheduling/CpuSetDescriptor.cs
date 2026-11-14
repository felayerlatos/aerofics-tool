namespace AeroficsLauncher;

public sealed class CpuSetDescriptor
{
	public uint Id { get; init; }

	public ushort Group { get; init; }

	public byte LogicalProcessorIndex { get; init; }

	public byte CoreIndex { get; init; }

	public byte LastLevelCacheIndex { get; init; }

	public byte NumaNodeIndex { get; init; }

	public byte EfficiencyClass { get; init; }

	public byte Flags { get; init; }

	public bool Parked => (Flags & 1) != 0;

    public bool Allocated => (Flags & 2) != 0;

	public bool RealTime => (Flags & 8) != 0;

	public override string ToString()
	{
		return $"CPU Set {Id} · CCD/L3 {LastLevelCacheIndex} · Core {CoreIndex} · LP {LogicalProcessorIndex}";
	}
}
