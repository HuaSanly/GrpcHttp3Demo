using GrpcHttp3Demo.Communication.Udp.Binding;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Communication.Udp.Server;

namespace GrpcHttp3Demo.Communication.Udp
{
    public static class UdpModule
    {
        public static IServiceCollection AddUdpModule(this IServiceCollection services)
        {
            services.AddSingleton<UdpMetricsService>();
            services.AddSingleton<UdpForwardingMetricsService>();
            services.AddSingleton<UdpSessionBindingService>();
            services.AddHostedService<UdpMediaServer>();
            return services;
        }
    }
}