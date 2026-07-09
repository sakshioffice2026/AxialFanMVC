using AxialFanMVC.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AxialFanMVC.Services
{
    public static class CurveCorrectionService
    {
        private static InferenceSession? _session;

        // NEW — lets callers (and the physics validator) know whether the
        // ONNX model actually loaded, instead of silently returning (0,0)
        // with no way to tell a real zero-correction result apart from
        // "the model was never there."
        public static bool IsModelAvailable => _session != null;

        public static void Initialize(string onnxModelPath, ILogger? logger = null)
        {
            if (File.Exists(onnxModelPath))
            {
                _session = new InferenceSession(onnxModelPath);
                logger?.LogInformation("PINN correction model loaded from {Path}", onnxModelPath);
            }
            else
            {
                // Previously silent. This is a real operational gap, not
                // just a log-noise concern — "PINN Corrected" curves will
                // be numerically identical to baseline with nothing in the
                // UI explaining why, unless this is surfaced loudly.
                logger?.LogWarning(
                    "PINN correction model not found at {Path} — all 'PINN Corrected' curves " +
                    "will be generated with zero ML correction (identical to Baseline) until this " +
                    "file is deployed.", onnxModelPath);
            }
            // _session stays null → Predict() falls back to zero correction.
            // The app must never break if the model isn't present yet —
            // it must instead be flagged, which is now handled in
            // PhysicsValidationEngine.
        }

        public static (double dPCorrection, double etaCorrection) Predict(PinnFeatureVector f, double q)
        {
            if (_session == null) return (0, 0);

            var input = new DenseTensor<float>(new[] { 1, 9 });
            input[0, 0] = (float)f.FlowCoefficient;
            input[0, 1] = (float)f.PressureCoefficient;
            input[0, 2] = (float)f.SpecificSpeed;
            input[0, 3] = (float)f.TipMachNumber;
            input[0, 4] = (float)f.Solidity;
            input[0, 5] = (float)f.ReynoldsNumber;
            input[0, 6] = f.MaxCamberPct.HasValue ? (float)f.MaxCamberPct.Value : 0f;
            input[0, 7] = f.MaxThicknessPct.HasValue ? (float)f.MaxThicknessPct.Value : 0f;
            input[0, 8] = (float)q;

            var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor("input", input) });
            var output = results.First().AsEnumerable<float>().ToArray();
            return (output[0], output[1]);
        }
    }
}