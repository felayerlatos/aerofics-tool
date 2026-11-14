using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

namespace SystemOptimizer.App.Configuration;

internal static class NvidiaProfilePresetCatalog
{
	private const string ResourcePrefix = "SystemOptimizer.App.Assets.NvidiaProfiles.";

	private static readonly (string FileName, string Description)[] Presets = new (string, string)[3]
	{
		("KernelOS 性能 v3.nip", "修改范围较小的性能取向预设"),
		("Sapphire.nip", "蓝宝石系统使用的全局性能预设"),
		("XOS.nip", "偏兼容与低延迟的 XOS 预设")
	};

	internal static IReadOnlyList<NvidiaProfilePresetOption> Load()
	{
		Assembly assembly = typeof(NvidiaProfilePresetCatalog).Assembly;
		List<NvidiaProfilePresetOption> list = new List<NvidiaProfilePresetOption>(Presets.Length);
		(string, string)[] presets = Presets;
		for (int i = 0; i < presets.Length; i++)
		{
			(string, string) tuple = presets[i];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			string name = "SystemOptimizer.App.Assets.NvidiaProfiles." + item;
			using Stream stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("内置 NVIDIA 预设资源缺失：" + item);
			using StreamReader streamReader = new StreamReader(stream, null, true, -1, false);
			string text = streamReader.ReadToEnd();
			list.Add(new NvidiaProfilePresetOption(Path.GetFileNameWithoutExtension(item), item2, text, CountSettings(text)));
		}
		return list;
	}

	private static int CountSettings(string xml)
	{
		XmlReaderSettings settings = new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			MaxCharactersInDocument = 1048576L
		};
		using StringReader input = new StringReader(xml);
		using XmlReader xmlReader = XmlReader.Create(input, settings);
		int num = 0;
		while (xmlReader.Read())
		{
			if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "ProfileSetting")
			{
				num++;
			}
		}
		return num;
	}
}
