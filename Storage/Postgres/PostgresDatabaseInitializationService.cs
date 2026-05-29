namespace GrpcHttp3Demo.Storage.Postgres
{
    public sealed class PostgresDatabaseInitializationService : IHostedService
    {
        private readonly PostgresDatabaseInitializer _initializer;

        public PostgresDatabaseInitializationService(PostgresDatabaseInitializer initializer)
        {
            _initializer = initializer;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _initializer.InitDatabase();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}