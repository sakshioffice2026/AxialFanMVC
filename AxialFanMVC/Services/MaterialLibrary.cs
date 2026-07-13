namespace AxialFanMVC.Services
{
    public class MaterialProperties
    {
        public string Name { get; set; } = "";
        public double DensityKgM3 { get; set; }
        public double YieldStrengthPa { get; set; }
        public double UltimateStrengthPa { get; set; }
        public double EnduranceLimitPaApprox { get; set; } // uncorrected — see StructCalcEngine note
        public string Source { get; set; } = "";
    }

    public static class MaterialLibrary
    {
        public const string DefaultMaterial = "Aluminum 6061-T6";

        // Values are handbook-typical for wrought/cast condition as noted.
        // Endurance limits are UNCORRECTED (no surface finish / notch / size
        // factors applied) — same caveat as flagged earlier for 6061-T6.
        private static readonly Dictionary<string, MaterialProperties> _materials = new()
        {
            ["Aluminum 6061-T6"] = new MaterialProperties
            {
                Name = "Aluminum 6061-T6",
                DensityKgM3 = 2700,
                YieldStrengthPa = 270e6,
                UltimateStrengthPa = 310e6,
                EnduranceLimitPaApprox = 96.5e6,
                Source = "MIL-HDBK-5H"
            },
            ["Aluminum 5052-H32"] = new MaterialProperties
            {
                Name = "Aluminum 5052-H32",
                DensityKgM3 = 2680,
                YieldStrengthPa = 193e6,
                UltimateStrengthPa = 228e6,
                EnduranceLimitPaApprox = 110e6,
                Source = "MIL-HDBK-5H"
            },
            ["Mild Steel A36"] = new MaterialProperties
            {
                Name = "Mild Steel A36",
                DensityKgM3 = 7850,
                YieldStrengthPa = 250e6,
                UltimateStrengthPa = 400e6,
                EnduranceLimitPaApprox = 186e6, // steel: true endurance limit exists, ~0.45-0.5*Su typical
                Source = "ASTM A36 / Shigley's typical steel Se ≈ 0.5*Su approximation"
            },
            ["Stainless Steel 304"] = new MaterialProperties
            {
                Name = "Stainless Steel 304",
                DensityKgM3 = 8000,
                YieldStrengthPa = 215e6,
                UltimateStrengthPa = 505e6,
                EnduranceLimitPaApprox = 240e6,
                Source = "ASM Handbook typical 304 SS"
            },
        };

        public static MaterialProperties Get(string? materialName)
        {
            if (!string.IsNullOrWhiteSpace(materialName) && _materials.TryGetValue(materialName, out var mat))
                return mat;
            return _materials[DefaultMaterial]; // safe fallback — matches prior hardcoded behavior
        }

        public static IEnumerable<string> AvailableMaterials => _materials.Keys;
    }
}