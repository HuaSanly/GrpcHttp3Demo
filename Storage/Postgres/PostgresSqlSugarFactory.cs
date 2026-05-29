using SqlSugar;

namespace GrpcHttp3Demo.Storage.Postgres
{
    public sealed class PostgresSqlSugarFactory
    {
        private readonly PostgresConnectionOptions _options;

        public PostgresSqlSugarFactory(PostgresConnectionOptions options)
        {
            _options = options;
        }

        public bool CanConnect()
        {
            return _options.Enabled && _options.HasRequiredValues();
        }

        public SqlSugarClient CreateClient()
        {
            return new SqlSugarClient(new ConnectionConfig
            {
                DbType = DbType.PostgreSQL,
                ConnectionString = _options.BuildConnectionString(),
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });
        }
    }
}