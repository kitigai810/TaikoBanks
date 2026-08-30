using System.Numerics;
using System.Text.RegularExpressions;

namespace TaikoNauts.Core.Taiko.Helper;

internal static class ComplexParser
{
    public static Complex Parse(string value)
    {
        value = value.Trim().Replace(" ", string.Empty);

        if (Regex.IsMatch(value, @"^[+-]?(\d+(\.\d+)?)?i$"))
        {
            var number = value.Replace("i", string.Empty);
            if (number is "" or "+") number = "1";
            if (number == "-") number = "-1";
            return new Complex(0, double.Parse(number));
        }

        if (double.TryParse(value, out var real))
        {
            return new Complex(real, 0);
        }

        var match = Regex.Match(value, @"^([+-]?\d+(\.\d+)?)([+-]\d+(\.\d+)?)i$");
        if (match.Success)
        {
            return new Complex(
                double.Parse(match.Groups[1].Value),
                double.Parse(match.Groups[3].Value));
        }

        throw new FormatException($"Invalid complex number: {value}");
    }
}
