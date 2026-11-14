using Microsoft.Win32;

namespace AeroficsLauncher;

internal static class MotherboardInfo
{
    public static string Read()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string product = Clean(bios?.GetValue("BaseBoardProduct") as string);
            string manufacturer = Clean(bios?.GetValue("BaseBoardManufacturer") as string);
            if (product.Length == 0) return "未识别";
            if (manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase)) manufacturer = "MSI";
            else if (manufacturer.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase)) manufacturer = "ASUS";
            else if (manufacturer.StartsWith("Gigabyte", StringComparison.OrdinalIgnoreCase)) manufacturer = "GIGABYTE";
            return manufacturer.Length == 0 || product.Contains(manufacturer, StringComparison.OrdinalIgnoreCase)
                ? product : manufacturer + " " + product;
        }
        catch (System.Security.SecurityException) { return "未识别"; }
        catch (UnauthorizedAccessException) { return "未识别"; }
        catch (IOException) { return "未识别"; }
    }

    private static string Clean(string? value)
    {
        value = value?.Trim() ?? "";
        return value.ToUpperInvariant() is "" or "DEFAULT STRING" or "TO BE FILLED BY O.E.M." or "TO BE FILLED BY OEM"
            or "NOT APPLICABLE" or "NONE" or "UNKNOWN" or "SYSTEM PRODUCT NAME" or "BASE BOARD PRODUCT NAME"
            ? "" : value;
    }
}
