using GrpcHttp3Demo.Models.Database;

namespace GrpcHttp3Demo.Storage.Postgres
{
    public sealed class PostgresDatabaseInitializer
    {
        private readonly PostgresConnectionOptions _options;
        private readonly PostgresSqlSugarFactory _factory;
        private readonly ILogger<PostgresDatabaseInitializer> _logger;

        public PostgresDatabaseInitializer(PostgresConnectionOptions options, PostgresSqlSugarFactory factory, ILogger<PostgresDatabaseInitializer> logger)
        {
            _options = options;
            _factory = factory;
            _logger = logger;
        }

        public void InitDatabase()
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Skip InitDatabase because Postgres is disabled.");
                return;
            }

            if (!_factory.CanConnect())
            {
                _logger.LogWarning("Skip InitDatabase because Postgres configuration is incomplete.");
                return;
            }

            var db = _factory.CreateClient();
            db.CodeFirst.InitTables(typeof(UserRecord), typeof(DeviceRecord), typeof(StreamCatalogRecord));
            _logger.LogInformation("InitDatabase completed for PostgreSQL schema.");
        }
    }
}