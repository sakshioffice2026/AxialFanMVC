using System.Globalization;
using System.Text.RegularExpressions;

namespace AxialFanMVC.Services.AeroAi
{
    public sealed class CustomOptions
    {
        public int? BladeCount { get; set; }

        public string? Material { get; set; }

        public double? MaxTipDiameterMm { get; set; }

        public string? DriveType { get; set; }

        public int? SpeedRpm { get; set; }

        public double? AltitudeM { get; set; }

        public double? TemperatureC { get; set; }

        public bool HasAny =>
            BladeCount.HasValue ||
            !string.IsNullOrEmpty(Material) ||
            MaxTipDiameterMm.HasValue ||
            !string.IsNullOrEmpty(DriveType) ||
            SpeedRpm.HasValue ||
            AltitudeM.HasValue ||
            TemperatureC.HasValue;

        public CustomOptions Clone() => (CustomOptions)MemberwiseClone();

        // Values present in "other" win; everything else is kept.
        public void MergeFrom(CustomOptions other)
        {
            if (other.BladeCount.HasValue) BladeCount = other.BladeCount;
            if (!string.IsNullOrEmpty(other.Material)) Material = other.Material;
            if (other.MaxTipDiameterMm.HasValue) MaxTipDiameterMm = other.MaxTipDiameterMm;
            if (!string.IsNullOrEmpty(other.DriveType)) DriveType = other.DriveType;
            if (other.SpeedRpm.HasValue) SpeedRpm = other.SpeedRpm;
            if (other.AltitudeM.HasValue) AltitudeM = other.AltitudeM;
            if (other.TemperatureC.HasValue) TemperatureC = other.TemperatureC;
        }

        public IReadOnlyList<string> Describe()
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new List<string>();

            if (BladeCount.HasValue)
                lines.Add("Blade Count: " + BladeCount.Value.ToString(inv) + " blades");

            if (!string.IsNullOrEmpty(Material))
                lines.Add("Material: " + Material);

            if (MaxTipDiameterMm.HasValue)
                lines.Add("Max Tip Diameter: " + MaxTipDiameterMm.Value.ToString("0.#", inv) + " mm");

            if (!string.IsNullOrEmpty(DriveType) || SpeedRpm.HasValue)
            {
                var drive = string.IsNullOrEmpty(DriveType) ? "Speed" : DriveType;
                var rpm = SpeedRpm.HasValue ? " at " + SpeedRpm.Value.ToString(inv) + " RPM" : string.Empty;
                lines.Add("Drive / Speed: " + drive + rpm);
            }

            if (AltitudeM.HasValue || TemperatureC.HasValue)
            {
                var parts = new List<string>();

                if (AltitudeM.HasValue)
                    parts.Add("Altitude " + AltitudeM.Value.ToString("0.#", inv) + " m");

                if (TemperatureC.HasValue)
                    parts.Add("Temp " + TemperatureC.Value.ToString("0.#", inv) + " °C");

                lines.Add("Ambient: " + string.Join(", ", parts));
            }

            return lines;
        }
    }

    public sealed class CustomOptionsParseResult
    {
        public CustomOptions Options { get; init; } = new();

        public bool IsSkip { get; init; }

        public string? Error { get; init; }
    }

    public static class CustomOptionsParser
    {
        public const string MatAluminum6061 = "Aluminum 6061-T6";
        public const string MatAluminum5052 = "Aluminum 5052-H32";
        public const string MatMildSteel = "Mild Steel A36";
        public const string MatStainless304 = "Stainless Steel 304";
        public const string MatStainless316 = "Stainless Steel 316";
        public const string MatFrp = "FRP / Composite";
        public const string MatPag = "PAG";

        public const string DriveDirect = "Direct Drive";
        public const string DriveVfd = "VFD";
        public const string DriveVBelt = "V-Belt";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        private static readonly Regex SkipRx = new(
            @"^\s*(skip|none|no changes?|nothing|leave (?:it )?as default|defaults?)\s*[.!]*\s*$",
            Opt);

        private static readonly Regex BladeKeyRx = new(
            @"(?:blade\s*count|blades?)\s*(?:of|is|=|:)?\s*(?<n>\d{1,2})\b",
            Opt);

        private static readonly Regex BladeSuffixRx = new(
            @"(?<![\d.])(?<n>\d{1,2})\s*-?\s*blades?\b",
            Opt);

        private static readonly Regex DiameterRx = new(
            @"(?:diameter|dia\.?|casing(?:\s*limit)?|max(?:imum)?\s*(?:tip\s*)?(?:diameter|dia\.?)?)\s*(?:limit|of|is|to|=|:)?\s*(?:max)?\s*(?<n>\d+(?:\.\d+)?)\s*(?<u>mm|cm|m|inches|inch|in)(?![a-z0-9/])",
            Opt);

        private static readonly Regex DiameterBareMmRx = new(
            @"(?<![\d.])(?<n>\d{3,4}(?:\.\d+)?)\s*mm\b",
            Opt);

        private static readonly Regex RpmRx = new(
            @"(?<![\d.])(?<n>\d{3,4})\s*rpm\b",
            Opt);

        private static readonly Regex AltitudeRx = new(
            @"\balt(?:itude)?\s*(?:of|is|=|:)?\s*(?<n>\d+(?:\.\d+)?)\s*(?<u>km|meters?|metres?|ft|feet|m)?(?![a-z0-9/])",
            Opt);

        private static readonly Regex TempKeyRx = new(
            @"\btemp(?:erature)?\s*(?:of|is|=|:)?\s*(?<n>-?\d+(?:\.\d+)?)\s*°?\s*(?<u>celsius|fahrenheit|c|f)?(?![a-z])",
            Opt);

        private static readonly Regex TempUnitRx = new(
            @"(?<![\w.])(?<n>-?\d+(?:\.\d+)?)\s*(?:°\s*(?<u1>c|f)|deg(?:rees?)?\s*(?<u2>c|f)|(?<u3>celsius|fahrenheit))(?![a-z])",
            Opt);

        private static readonly (Regex Rx, string Name)[] MaterialRules =
        {
            (new Regex(@"\b5052\b", Opt), MatAluminum5052),
            (new Regex(@"\b6061\b", Opt), MatAluminum6061),
            (new Regex(@"\ba\s*-?\s*36\b|mild\s*steel|carbon\s*steel|\bmild\b", Opt), MatMildSteel),
            (new Regex(@"\b(?:ss|stainless(?:\s*steel)?)\s*-?\s*304\b|\b304\b(?!\s*mm)", Opt), MatStainless304),
            (new Regex(@"\b(?:ss|stainless(?:\s*steel)?)\s*-?\s*316\b|\b316\b(?!\s*mm)|stainless", Opt), MatStainless316),
            (new Regex(@"\bfrp\b|\bgrp\b|composite|fib(?:re|er)\s*glass|fiberglass", Opt), MatFrp),
            (new Regex(@"\bpag\b|polyamide|nylon", Opt), MatPag),
            (new Regex(@"alumin(?:i)?um|\bal\b", Opt), MatAluminum6061),
            (new Regex(@"\bsteel\b", Opt), MatMildSteel)
        };

        private static readonly Regex VfdRx = new(@"\bvfd\b|inverter|variable\s*(?:frequency|speed)", Opt);

        private static readonly Regex BeltRx = new(@"v\s*-?\s*belt|\bbelt\b", Opt);

        private static readonly Regex DirectRx = new(@"\bdirect(?:\s*drive)?\b", Opt);

        public static CustomOptionsParseResult Parse(string? text)
        {
            var s = (text ?? string.Empty).ToLowerInvariant();

            if (SkipRx.IsMatch(s))
                return new CustomOptionsParseResult { IsSkip = true };

            var o = new CustomOptions();
            string? error = null;

            // Blade count
            var m = BladeKeyRx.Match(s);
            if (!m.Success)
                m = BladeSuffixRx.Match(s);

            if (m.Success)
            {
                var n = int.Parse(m.Groups["n"].Value, Inv);
                if (n < 3 || n > 20)
                    error ??= "Blade count must be between 3 and 20.";
                else
                    o.BladeCount = n;
            }

            // Material
            foreach (var (rx, name) in MaterialRules)
            {
                if (rx.IsMatch(s))
                {
                    o.Material = name;
                    break;
                }
            }

            // Maximum tip diameter
            m = DiameterRx.Match(s);
            double? diameterMm = null;

            if (m.Success)
            {
                diameterMm = ToMm(ParseNumber(m.Groups["n"].Value), m.Groups["u"].Value);
            }
            else
            {
                m = DiameterBareMmRx.Match(s);
                if (m.Success)
                    diameterMm = ParseNumber(m.Groups["n"].Value);
            }

            if (diameterMm.HasValue)
            {
                if (diameterMm.Value < 250 || diameterMm.Value > 4000)
                    error ??= "Max tip diameter must be between 250 and 4000 mm.";
                else
                    o.MaxTipDiameterMm = Math.Round(diameterMm.Value, 1);
            }

            // Drive type and speed
            if (VfdRx.IsMatch(s))
                o.DriveType = DriveVfd;
            else if (BeltRx.IsMatch(s))
                o.DriveType = DriveVBelt;
            else if (DirectRx.IsMatch(s))
                o.DriveType = DriveDirect;

            m = RpmRx.Match(s);
            if (m.Success)
            {
                var rpm = int.Parse(m.Groups["n"].Value, Inv);
                if (rpm < 300 || rpm > 3600)
                    error ??= "Speed must be between 300 and 3600 RPM.";
                else
                    o.SpeedRpm = rpm;
            }

            // Altitude
            m = AltitudeRx.Match(s);
            if (m.Success)
            {
                var value = ParseNumber(m.Groups["n"].Value);
                var unit = m.Groups["u"].Value;

                var meters = unit switch
                {
                    "km" => value * 1000.0,
                    "ft" or "feet" => value * 0.3048,
                    _ => value
                };

                if (meters < 0 || meters > 5000)
                    error ??= "Altitude must be between 0 and 5000 m.";
                else
                    o.AltitudeM = Math.Round(meters, 1);
            }

            // Ambient temperature
            double? tempC = null;

            m = TempKeyRx.Match(s);
            if (m.Success)
            {
                tempC = ToCelsius(ParseNumber(m.Groups["n"].Value), m.Groups["u"].Value);
            }
            else
            {
                m = TempUnitRx.Match(s);
                if (m.Success)
                {
                    var unit = m.Groups["u1"].Success ? m.Groups["u1"].Value
                             : m.Groups["u2"].Success ? m.Groups["u2"].Value
                             : m.Groups["u3"].Value;

                    tempC = ToCelsius(ParseNumber(m.Groups["n"].Value), unit);
                }
            }

            if (tempC.HasValue)
            {
                if (tempC.Value < -40 || tempC.Value > 120)
                    error ??= "Ambient temperature must be between -40 and 120 °C.";
                else
                    o.TemperatureC = Math.Round(tempC.Value, 1);
            }

            return new CustomOptionsParseResult { Options = o, Error = error };
        }

        private static double ParseNumber(string text)
            => double.Parse(text, NumberStyles.Float, Inv);

        private static double ToMm(double value, string unit) => unit switch
        {
            "cm" => value * 10.0,
            "m" => value * 1000.0,
            "in" or "inch" or "inches" => value * 25.4,
            _ => value
        };

        private static double ToCelsius(double value, string unit)
        {
            if (unit == "f" || unit == "fahrenheit")
                return (value - 32.0) * 5.0 / 9.0;

            return value;
        }
    }
}