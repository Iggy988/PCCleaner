using System.Globalization;
using PCCleaner.Services.Localization;

namespace PCCleaner.Services;

public static class ByteSize
{
    // Unit labels are localized (e.g. "КБ" in Russian); numbers use the current culture's separators.
    private static readonly string[] UnitKeys = ["Unit_B", "Unit_KB", "Unit_MB", "Unit_GB", "Unit_TB"];

    public static string Format(long bytes) => Format((double)bytes);

    public static string Format(double bytes)
    {
        if (bytes <= 0) return "0 " + Loc.T(UnitKeys[0]);

        var unit = 0;
        while (bytes >= 1024 && unit < UnitKeys.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        var format = unit == 0 || bytes >= 100 ? "0" : bytes >= 10 ? "0.#" : "0.##";
        return bytes.ToString(format, CultureInfo.CurrentCulture) + " " + Loc.T(UnitKeys[unit]);
    }
}
