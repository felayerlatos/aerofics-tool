using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AeroficsScheduler;

internal static class SchedulerProtection
{
	private static readonly byte[] Key = Convert.FromBase64String("WmQeGfN3p3pZ7bZ4VQfJxQ8JrH5m7c8X3bV4n2L9s0A=");

	internal static void Verify()
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aerofics.Scheduler.Marker") ?? throw new InvalidDataException("保护资源缺失");
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		byte[] array = memoryStream.ToArray();
		if (array.Length < 28)
		{
			throw new InvalidDataException("保护资源损坏");
		}
		byte[] array2 = new byte[array.Length - 28];
		using AesGcm aesGcm = new AesGcm(Key, 16);
		aesGcm.Decrypt(array.AsSpan(0, 12), array.AsSpan(28), array.AsSpan(12, 16), array2);
		if (!Encoding.UTF8.GetString(array2).Equals("AEROFICS-SCHEDULER-V1", StringComparison.Ordinal))
		{
			throw new InvalidDataException("保护资源校验失败");
		}
		CryptographicOperations.ZeroMemory(array2);
	}
}
