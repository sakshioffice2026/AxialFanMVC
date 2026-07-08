using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AxialFanMVC.Services
{
    public static class CurveCorrectionService
    {
        private static InferenceSession? _session;

        public static void Initialize(string onnxModelPath)
        {
            if (File.Exists(onnxModelPath))
                _session = new InferenceSession(onnxModelPath);
            // Missing file → _session stays null → Predict() falls back
            // to zero correction. The app must never break if the model
            // isn't present yet.
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