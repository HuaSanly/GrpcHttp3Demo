using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Storage.Memory
{
    public sealed class MemoryStorageCatalog
    {
        private readonly SessionMemoryStore _sessionMemory;

        public MemoryStorageCatalog(SessionMemoryStore sessionMemory)
        {
            _sessionMemory = sessionMemory;
        }

        public IReadOnlyList<string> DeclaredStores { get; } = new[]
        {
            "SessionMemoryStore: sessions, identity index, pairing index, subscription index, UDP endpoint indexes, forwarding hot tables, feedback route, P2P keys"
        };

        public object Snapshot()
        {
            return new
            {
                stores = DeclaredStores,
                session = _sessionMemory.Snapshot()
            };
        }
    }
}
