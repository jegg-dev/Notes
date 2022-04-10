using System.Globalization;
using Windows.UI;

namespace Notes
{
    internal class Tools
    {
        public static Color GetColorFromHexcode(string hexCode)
        {
            hexCode = hexCode.TrimStart('#');

            Color col;
            if (hexCode.Length == 6)
                col = Color.FromArgb(255, // hardcoded opaque
                            (byte)int.Parse(hexCode.Substring(0, 2), NumberStyles.HexNumber),
                            (byte)int.Parse(hexCode.Substring(2, 2), NumberStyles.HexNumber),
                            (byte)int.Parse(hexCode.Substring(4, 2), NumberStyles.HexNumber));
            else // assuming length of 8
                col = Color.FromArgb(
                            (byte)int.Parse(hexCode.Substring(0, 2), NumberStyles.HexNumber),
                            (byte)int.Parse(hexCode.Substring(2, 2), NumberStyles.HexNumber),
                            (byte)int.Parse(hexCode.Substring(4, 2), NumberStyles.HexNumber),
                            (byte)int.Parse(hexCode.Substring(6, 2), NumberStyles.HexNumber));

            return col;
        }
    }
}
