using AxialFanMVC.Models;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IPhysicsValidationEngine
    {
        PhysicsValidationResult Validate(
            PerformanceCurveData curve,
            PinnFeatureVector features,
            PhysicsValidationContext context);
    }
}