using AxialFanMVC.Database;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // SoundCalcEngine — axial fan acoustic prediction
    //
    // Formulas sourced from:
    //   - ASHRAE Handbook of Fundamentals, Ch.48 (broadband Lw)
    //   - ISO 13347-1/2 (octave bands, specific sound power)
    //   - AMCA Bulletin 300 (specific sound level, inlet/outlet split)
    //   - Lowson (1970) / Gutin (1948) — rotor tonal (BPF) noise
    //   - ISO 3745 (free-field Lp at distance)
    //   - ISO 3744 / AMCA 300 (directivity factor Q by installation type)
    //   - ISO 1996 (NR curve rating)
    //   - IEC 61672 (A-weighting corrections)
    //
    // NOTE: not cross-checked against the Bleier "Fan Handbook" RAG
    // extract — that extract has no acoustics chapter. Treat outputs
    // as an engineering estimate, not a certified acoustic test result.
    // ═══════════════════════════════════════════════════════════════
    public static class SoundCalcEngine
    {
        // Standard octave band centres, 63 Hz – 8 kHz
        private static readonly int[] BandCentres = { 63, 125, 250, 500, 1000, 2000, 4000, 8000 };

        // IEC 61672 A-weighting correction per band
        private static readonly double[] AWeightingDb = { -26.2, -16.1, -8.6, -3.2, 0.0, 1.2, 1.0, -1.1 };

        // ISO 1996 NR curve coefficients: NR = (Lp[i] - a[i]) / b[i]
        private static readonly double[] NrA = { 55.4, 35.5, 22.0, 12.0, 4.8, 0.0, -3.5, -6.1 };
        private static readonly double[] NrB = { 0.681, 0.790, 0.870, 0.930, 0.974, 1.000, 1.015, 1.025 };

        // ── Directivity factor Q / Directivity Index by installation
        // environment (ISO 3744 / AMCA 300). "Reverberant Room" is a
        // deliberately conservative stand-in — a true reverberant-field
        // calc needs room volume + absorption (Sabine equation), which
        // this app does not currently collect.
        private static readonly Dictionary<string, (double Q, double DiDb)> EnvironmentAcoustics = new()
        {
            ["Free Field"] = (1.0, 0.0),
            ["Outdoor"] = (2.0, 3.0),
            ["Semi-Reverberant"] = (4.0, 6.0),
            ["Industrial Plant"] = (8.0, 9.0),
            ["Reverberant Room"] = (16.0, 12.0),
        };

        private const string DefaultEnvironment = "Free Field";

        // Standard fallback values (ASHRAE HOF Ch.48 / AMCA 300) used
        // whenever the wizard field is left at its unset/zero state —
        // i.e. these are backend defaults, not overrides of a genuine
        // user-entered number. If the user has typed a nonzero value,
        // that value always wins.
        private const double DefaultCasingTransmissionLossDb = 18.0; // unlagged sheet-metal casing
        private const double DefaultSilencerAttenuationDbWhenFitted = 15.0; // ~1.5 m dissipative silencer
        private const double DefaultRoomCorrectionDbReverberant = 6.0; // conservative reverberant-room estimate

        public static double GetDirectivityIndexDb(string? environment)
        {
            if (environment != null && EnvironmentAcoustics.TryGetValue(environment, out var v))
                return v.DiDb;
            return EnvironmentAcoustics[DefaultEnvironment].DiDb;
        }

        public static double GetDirectivityFactorQ(string? environment)
        {
            if (environment != null && EnvironmentAcoustics.TryGetValue(environment, out var v))
                return v.Q;
            return EnvironmentAcoustics[DefaultEnvironment].Q;
        }

        // Effective casing transmission loss: user value if set (>0),
        // otherwise the standard unlagged sheet-metal default.
        public static double GetEffectiveCasingTransmissionLossDb(DesignInput d) =>
     (d.CasingTransmissionLossDb.HasValue && d.CasingTransmissionLossDb.Value > 0)
         ? d.CasingTransmissionLossDb.Value
         : DefaultCasingTransmissionLossDb;

        public static double GetEffectiveSilencerAttenuationDb(DesignInput d)
        {
            if (d.SilencerAttenuationDb.HasValue && d.SilencerAttenuationDb.Value > 0)
                return d.SilencerAttenuationDb.Value;
            return d.AccSilencer ? DefaultSilencerAttenuationDbWhenFitted : 0.0;
        }

        public static double GetEffectiveRoomCorrectionDb(DesignInput d)
        {
            if (d.RoomCorrectionDb.HasValue && d.RoomCorrectionDb.Value != 0)
                return d.RoomCorrectionDb.Value;
            return d.AcousticEnvironment == "Reverberant Room" ? DefaultRoomCorrectionDbReverberant : 0.0;
        }

        public static SoundCalcResult Calculate(DesignInput d, AeroCalcResult aero)
        {
            var r = new SoundCalcResult();

            // 1. Geometry / operating point (reuses AeroCalcEngine's output —
            //    same tip/hub radius, chord, shaft power, efficiency as the
            //    aerodynamic card on the Results page, so numbers stay consistent).
            double tipRadius = d.TipDiameterMm / 2000.0;
            double omega = 2 * Math.PI * d.SpeedRpm / 60.0;
            double tipSpeed = omega * tipRadius;

            // Speed of sound from actual inlet temperature.
            double speedOfSound = 331.3 * Math.Sqrt(1.0 + d.TemperatureCelsius / 273.15);
            double mTip = tipSpeed / speedOfSound;

            double bpf = d.BladeCount * d.SpeedRpm / 60.0;

            r.TipSpeedMs = tipSpeed;
            r.TipMachNumber = mTip;
            r.BpfHz = bpf;
            r.Bpf2Hz = bpf * 2;
            r.Bpf3Hz = bpf * 3;

            // 2. Broadband sound power level (ASHRAE Fundamentals Ch.48 "Fan
            //    Sound Power Level Estimation" method):
            //    Lw = Kw + 10*log10(Q_cfm) + 20*log10(dP_inWg)
            //
            //    IMPORTANT — this method's Kw table is calibrated in
            //    IMPERIAL units (airflow in CFM, pressure in inches of
            //    water gauge), NOT SI. A previous version of this engine
            //    fed raw m³/s and Pa directly into this formula with an
            //    imperial-calibrated Kw, which inflated Lw by ~35–45 dB
            //    (the 20*log10 pressure term is extremely sensitive to
            //    the numeric magnitude/units of its argument). Converting
            //    to the units the method actually expects fixes this.
            //
            //    Kw below (37 dB) is a representative mid-range value for
            //    axial-flow fans generally (ASHRAE/AMCA tables give
            //    roughly: vaneaxial ~30–35, tubeaxial ~35–40, propeller
            //    ~40–48 — this project does not currently capture which
            //    axial sub-type is being modeled, so a single
            //    representative constant is used). VALIDATE against an
            //    AMCA-certified sound test report for your actual fan
            //    type before using this for compliance or purchasing
            //    decisions — this is an engineering estimate, not a
            //    certified figure.
            const double Kw = 37.0;
            const double CfmPerM3s = 2118.88;   // 1 m³/s = 2118.88 CFM
            const double InWgPerPa = 1.0 / 249.089; // 1 Pa = 1/249.089 in.w.g.

            double q = d.FlowRateM3s;
            double dP = d.TotalPressurePa;

            double qCfm = q * CfmPerM3s;
            double dpInWg = Math.Max(dP * InWgPerPa, 0.001); // guard against log(0)

            double lwBase = Kw + 10.0 * Math.Log10(qCfm) + 20.0 * Math.Log10(dpInWg);
            double machCorr = mTip > 0.1 ? 60.0 * Math.Log10(mTip / 0.1) : 0.0;

            // Efficiency correction — poorer efficiency radiates more noise (ISO 13347)
            double etaFraction = Math.Clamp(aero.OverallEfficiencyPct / 100.0, 0.01, 1.0);
            double etaCorr = -10.0 * Math.Log10(etaFraction);

            double lwTotal = lwBase + machCorr + etaCorr;

            // 3. Octave-band spectral shape (ASHRAE HOF Ch.48 axial-fan spectrum,
            //    shifted by RPM, with a BPF tonal boost in its containing band)
            double[] baseSpectrum = { -5.0, -2.0, 0.0, 0.0, -1.0, -3.0, -6.0, -10.0 };
            double rpmRatio = Math.Log10(d.SpeedRpm / 1000.0);
            double[] spectrum = baseSpectrum.Select((v, i) => v + rpmRatio * (i - 3.5) * 0.8).ToArray();

            int bpfBandIdx = GetOctaveBandIndex(bpf);
            double bpfBoost = Math.Max(
                20.0 * Math.Log10(d.BladeCount) + 60.0 * Math.Log10(Math.Max(mTip, 0.01)) + 15.0, 0.0);
            spectrum[bpfBandIdx] += bpfBoost;

            double[] lwBands = spectrum.Select(offset => lwTotal + offset).ToArray();
            r.OctaveBandLwDb = lwBands;

            // 4. Overall levels — logarithmic (energy) sum across bands
            r.LwOverallDb = LogSum(lwBands);
            r.LwOverallDba = LogSum(ApplyAWeighting(lwBands));

            // 5. Sound pressure at receiver distance (ISO 3745):
            //    Lp = Lw + DI - 10*log10(4*pi*r^2) - attenuation + room correction
            //
            //    DI, casing TL, silencer attenuation, and room correction
            //    are all resolved server-side via the Get*/GetEffective*
            //    helpers above rather than trusting the raw wizard field
            //    directly — each helper falls back to a standard value
            //    when the field is left unset (0), but always respects a
            //    genuine nonzero user entry.
            double directivityIndexDb = GetDirectivityIndexDb(d.AcousticEnvironment);
            double casingTransmissionLossDb = GetEffectiveCasingTransmissionLossDb(d);
            double silencerAttenuationDb = GetEffectiveSilencerAttenuationDb(d);
            double roomCorrectionDb = GetEffectiveRoomCorrectionDb(d);
            double receiverDistanceM = (d.ReceiverDistanceM.HasValue && d.ReceiverDistanceM.Value > 0)
                ? d.ReceiverDistanceM.Value
                : 1.0;
            double geomAtten = 10.0 * Math.Log10(4.0 * Math.PI * receiverDistanceM * receiverDistanceM);

            double totalLoss =
      (d.InletAttenuationDb ?? 0.0)
    + (d.OutletAttenuationDb ?? 0.0)
    + casingTransmissionLossDb
    + silencerAttenuationDb;

            double[] lpBands = lwBands
                .Select(v =>
                    v
                    + directivityIndexDb
                    - geomAtten
                    - totalLoss
                    + roomCorrectionDb)
                .ToArray();

            r.LpOverallDb = LogSum(lpBands);
            r.LpOverallDba = LogSum(ApplyAWeighting(lpBands));

            r.DirectivityIndexDbUsed = directivityIndexDb;
            r.CasingTransmissionLossDbUsed = casingTransmissionLossDb;
            r.SilencerAttenuationDbUsed = silencerAttenuationDb;
            r.RoomCorrectionDbUsed = roomCorrectionDb;

            // 6. AMCA 300 specific sound level: Ks = Lw(A) - 10log10(Q) - 20log10(dP)
            r.SpecificSoundLevelKs = r.LwOverallDba - 10.0 * Math.Log10(q) - 20.0 * Math.Log10(dP);

            // 7. NR curve rating (ISO 1996) — highest NR touched by any band's Lp
            double nrMax = 0;
            for (int i = 0; i < 8; i++)
                nrMax = Math.Max(nrMax, (lpBands[i] - NrA[i]) / NrB[i]);
            nrMax = Math.Round(nrMax);
            r.NrValue = nrMax;
            r.NoiseRating = nrMax switch
            {
                <= 25 => "NR-25 (Broadcast studio)",
                <= 35 => "NR-35 (Conference room)",
                <= 45 => "NR-45 (Private office)",
                <= 55 => "NR-55 (Open office)",
                <= 65 => "NR-65 (Light industrial)",
                <= 75 => "NR-75 (Industrial workshop)",
                _ => "NR-85+ (Heavy industrial)"
            };

            // 8. Warnings
            if (mTip > 0.7)
                r.Warnings.Add($"Blade tip Mach number {mTip:F2} exceeds 0.7 — transonic noise rises sharply. Consider reducing RPM or tip diameter.");

            if (bpf > 2000)
                r.Warnings.Add($"Blade passing frequency {bpf:F0} Hz falls in the most noise-sensitive hearing range — consider fewer blades or lower RPM.");

            if (d.BladeCount % 2 == 0)
                r.Warnings.Add("Even blade count reinforces blade-passing tonal harmonics — an odd count is typically quieter for the same duty.");

            if (d.MaxNoiseDbA.HasValue)
            {
                double allowable = d.MaxNoiseDbA.Value - (d.SafetyMarginDb ?? 3.0);
                if (r.LpOverallDba > allowable)
                {
                    r.Warnings.Add(
                        $"Predicted sound pressure ({r.LpOverallDba:F1} dB(A)) exceeds allowable limit ({allowable:F1} dB(A)).");
                }
            }

            return r;
        }

        private static int GetOctaveBandIndex(double f)
        {
            for (int i = 0; i < BandCentres.Length - 1; i++)
                if (f < BandCentres[i] * Math.Sqrt(2.0)) return i;
            return 7;
        }

        private static double[] ApplyAWeighting(double[] bands) =>
            bands.Select((v, i) => v + AWeightingDb[i]).ToArray();

        private static double LogSum(double[] bands) =>
            10.0 * Math.Log10(bands.Sum(v => Math.Pow(10.0, v / 10.0)));
    }

    // ─────────────────────────────────────────────
    // Result transfer object (no EF dependencies) — same pattern as
    // AeroCalcResult / StructCalcResult in CalcEngines.cs
    // ─────────────────────────────────────────────
    public class SoundCalcResult
    {
        public double LwOverallDb { get; set; }
        public double LwOverallDba { get; set; }
        public double LpOverallDb { get; set; }
        public double LpOverallDba { get; set; }
        public double SpecificSoundLevelKs { get; set; }

        public double BpfHz { get; set; }
        public double Bpf2Hz { get; set; }
        public double Bpf3Hz { get; set; }
        public double TipSpeedMs { get; set; }
        public double TipMachNumber { get; set; }

        // Values actually used in the Lp calc after backend fallback
        // resolution — expose these so the Results view can show the
        // real numbers instead of stale/zero wizard fields.
        public double DirectivityIndexDbUsed { get; set; }
        public double CasingTransmissionLossDbUsed { get; set; }
        public double SilencerAttenuationDbUsed { get; set; }
        public double RoomCorrectionDbUsed { get; set; }

        public double NrValue { get; set; }
        public string NoiseRating { get; set; } = "";

        // 8 values, 63Hz–8kHz, unweighted Lw
        public double[] OctaveBandLwDb { get; set; } = new double[8];

        public List<string> Warnings { get; set; } = new();
    }
}