using Microsoft.SemanticKernel;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IKernelFactory
    {
        Kernel CreateKernel();
    }
}