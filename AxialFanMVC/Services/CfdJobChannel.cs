using System.Threading.Channels;

namespace AxialFanMVC.Services
{
    public interface ICfdJobSignal
    {
        void NotifyJobQueued(int jobId);
    }

    public class CfdJobChannel : ICfdJobSignal
    {
        private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
        public ChannelReader<int> Reader => _channel.Reader;
        public void NotifyJobQueued(int jobId) => _channel.Writer.TryWrite(jobId);
    }
}
