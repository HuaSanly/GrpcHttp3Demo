using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Communication.Grpc.Push
{
    public interface IEventStreamSender
    {
        Task WriteAsync(EventMessage message);
    }
}

