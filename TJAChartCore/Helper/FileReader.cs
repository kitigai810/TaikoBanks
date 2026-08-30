using System.Text;

namespace TaikoNauts.Core.Taiko.Helper;

internal static class FileReader
{
    public static string[] ReadShiftJisLines(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return File.ReadAllLines(path, Encoding.GetEncoding("shift_jis"));
    }

    public static string ReadTextAuto(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("shift_jis").GetString(bytes);
        }
    }
}
