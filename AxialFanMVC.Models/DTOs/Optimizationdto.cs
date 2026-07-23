namespace AxialFanMVC.Models
{
    // Mirrors optimizer_service.py's OptimizeRequest field-for-field —
    // property names use System.Text.Json's default camelCase-insensitive
    // matching against Python's snake_case via JsonPropertyName below, so
    // the contract stays explicit instead of relying on naming luck.
    public class OptimizeRequestDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("flow_rate_m3s")]
        public double FlowRateM3s { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total_pressure_pa")]
        public double TotalPressurePa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("temperature_celsius")]
        public double TemperatureCelsius { get; set; } = 25.0;

        [System.Text.Json.Serialization.JsonPropertyName("min_efficiency_pct")]
        public double? MinEfficiencyPct { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_noise_dba")]
        public double? MaxNoiseDbA { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_motor_power_kw")]
        public double? MaxMotorPowerKw { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_tip_diameter_mm")]
        public double? MaxTipDiameterMm { get; set; }
    }

    public class OptimizeCandidateDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("tip_diameter_mm")]
        public double TipDiameterMm { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hub_ratio")]
        public double HubRatio { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("blade_angle_deg")]
        public double BladeAngleDeg { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("blade_count")]
        public int BladeCount { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("speed_rpm")]
        public double SpeedRpm { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("blade_profile")]
        public string BladeProfile { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("predicted_efficiency_pct")]
        public double PredictedEfficiencyPct { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("predicted_noise_dba")]
        public double PredictedNoiseDbA { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("predicted_cost_total")]
        public double PredictedCostTotal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("predicted_safety_factor")]
        public double PredictedSafetyFactor { get; set; }
    }
}