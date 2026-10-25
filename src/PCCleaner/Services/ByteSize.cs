using System.Globalization;

namespace PCCleaner.Services;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes) => Format((double)bytes);

    public static string Format(double bytes)
    {
        if (bytes <= 0) return "0 B";

        var unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        var format = unit == 0 || bytes >= 100 ? "0" : bytes >= 10 ? "0.#" : "0.##";
        return bytes.ToString(format, CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
