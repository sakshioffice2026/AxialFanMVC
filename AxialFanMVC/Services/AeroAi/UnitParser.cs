using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AxialFanMVC.Services.AeroAi
{
    public sealed record ParsedQuantity(double Value, string Unit, double SiValue, bool IsConverted);

    public static class UnitParser
    {
        private const double CfmToM3s = 0.0004719474432;
        private const double InWgToPa = 249.08891;
        private const double MmWgToPa = 9.80665;
        private const double PsiToPa = 6894.757;

        private static readonly Regex FlowRx = new(
            @"(?<num>\d+(?:\.\d+)?|\.\d+)\s*(?<unit>cfm|m3/s|m3/h|m3/min|m3s|m3h|cmh|cmm|cms|l/s|lps)(?![a-z0-9/])",
            RegexOptions.Compiled);

        private static readonly Regex PressureRx = new(
            @"(?<num>\d+(?:\.\d+)?|\.\d+)\s*(?<unit>inwg|mmwg|kpa|pascals?|pa|mbar|psi)(?![a-z0-9/])",
            RegexOptions.Compiled);

        private static readonly Regex BareRx = new(
            @"^\s*(?<num>\d+(?:\.\d+)?|\.\d+)\s*$",
            RegexOptions.Compiled);

        public static bool TryParseFlow(string? text, bool allowBareNumber, [NotNullWhen(true)] out ParsedQuantity? quantity)
        {
            quantity = null;
            var s = Normalize(text);

            var m = FlowRx.Match(s);
            if (m.Success)
            {
                var v = ParseNumber(m.Groups["num"].Value);
                double si;
                string label;
                bool converted;

                switch (m.Groups["unit"].Value)
                {
                    case "cfm":
                        si = v * CfmToM3s; label = "CFM"; converted = true; break;
                    case "m3/s":
                    case "m3s":
                    case "cms":
                        si = v; label = "m³/s"; converted = false; break;
                    case "m3/h":
                    case "m3h":
                    case "cmh":
                        si = v / 3600.0; label = "m³/h"; converted = true; break;
                    case "m3/min":
                    case "cmm":
                        si = v / 60.0; label = "m³/min"; converted = true; break;
                    case "l/s":
                    case "lps":
                        si = v / 1000.0; label = "L/s"; converted = true; break;
                    default:
                        return false;
                }

                quantity = new ParsedQuantity(v, label, si, converted);
                return true;
            }

            if (allowBareNumber)
            {
                var b = BareRx.Match(s);
                if (b.Success)
                {
                    var v = ParseNumber(b.Groups["num"].Value);
                    quantity = new ParsedQuantity(v, "m³/s", v, false);
                    return true;
                }
            }

            return false;
        }

        public static bool TryParsePressure(string? text, bool allowBareNumber, [NotNullWhen(true)] out ParsedQuantity? quantity)
        {
            quantity = null;
            var s = Normalize(text);

            var m = PressureRx.Match(s);
            if (m.Success)
            {
                var v = ParseNumber(m.Groups["num"].Value);
                double si;
                string label;
                bool converted;

                switch (m.Groups["unit"].Value)
                {
                    case "pa":
                    case "pascal":
                    case "pascals":
                        si = v; label = "Pa"; converted = false; break;
                    case "kpa":
                        si = v * 1000.0; label = "kPa"; converted = true; break;
                    case "inwg":
                        si = v * InWgToPa; label = "inWG"; converted = true; break;
                    case "mmwg":
                        si = v * MmWgToPa; label = "mmWG"; converted = true; break;
                    case "mbar":
                        si = v * 100.0; label = "mbar"; converted = true; break;
                    case "psi":
                        si = v * PsiToPa; label = "psi"; converted = true; break;
                    default:
                        return false;
                }

                quantity = new ParsedQuantity(v, label, si, converted);
                return true;
            }

            if (allowBareNumber)
            {
                var b = BareRx.Match(s);
                if (b.Success)
                {
                    var v = ParseNumber(b.Groups["num"].Value);
                    quantity = new ParsedQuantity(v, "Pa", v, false);
                    return true;
                }
            }

            return false;
        }

        private static double ParseNumber(string text)
            => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string Normalize(string? text)
        {
            var s = (text ?? string.Empty).ToLowerInvariant();

            s = s.Replace('³', '3').Replace("m^3", "m3");

            // Strip thousands-separator commas that are NOT field separators
            // e.g. "12,000 cfm" → "12000 cfm"  but "flow=10,pressure=600" is left alone
            s = Regex.Replace(s, @"(?<=\d),(?=\d{3}(?!\d))", string.Empty);

            // Normalise keyword=value separators: "flow rate =10", "pressure: 600", "pressure-1200"
            // so the number is adjacent to its unit for FlowRx / PressureRx to match.
            s = Regex.Replace(s,
                @"(?:flow\s*rate?|volume\s*flow(?:\s*rate)?|q)\s*[-=:]\s*",
                string.Empty);
            s = Regex.Replace(s,
                @"(?:total\s*pressure|static\s*pressure|pressure|p)\s*[-=:]\s*",
                string.Empty);

            s = Regex.Replace(s, @"cubic\s*feet\s*(?:per|/)\s*min(?:ute)?s?", "cfm");
            s = Regex.Replace(s, @"cubic\s*(?:meters?|metres?)", "m3");
            s = Regex.Replace(s, @"\b(?:liters?|litres?)\b", "l");

            s = Regex.Replace(s, @"\s*/\s*", "/");
            s = Regex.Replace(s, @"\s+per\s+(?:second|sec)\b", "/s");
            s = Regex.Replace(s, @"\s+per\s+(?:hour|hr)\b", "/h");
            s = Regex.Replace(s, @"\s+per\s+(?:minute|min)\b", "/min");
            s = Regex.Replace(s, @"/(?:second|sec)\b", "/s");
            s = Regex.Replace(s, @"/(?:hour|hrs?)\b", "/h");
            s = Regex.Replace(s, @"/(?:minute|mins?)\b", "/min");

            s = Regex.Replace(s, @"\bw\.g\.?", "wg");
            s = Regex.Replace(s, @"(?<![a-z])(?:inches|inch|in)\.?\s*(?:of\s*)?(?:water\s*gauge|water\s*column|water|wg|wc|h2o)", "inwg");
            s = Regex.Replace(s, @"(?<![a-z])mm\.?\s*(?:of\s*)?(?:water\s*gauge|water\s*column|water|wg|wc|h2o)", "mmwg");
            s = Regex.Replace(s, "\"\\s*wg", "inwg");
            s = Regex.Replace(s, @"\biwg\b", "inwg");
            s = Regex.Replace(s, @"\binh2o\b", "inwg");

            return s;
        }
    }
}