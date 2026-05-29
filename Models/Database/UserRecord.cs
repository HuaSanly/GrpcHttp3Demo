using SqlSugar;

namespace GrpcHttp3Demo.Models.Database
{
    public enum UserRole
    {
        Admin = 1,
        Operator = 2,
        Viewer = 3
    }

    [SugarTable("users")]
    public sealed class UserRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(Length = 64, IsNullable = false)]
        public string Username { get; set; } = string.Empty;

        [SugarColumn(Length = 512, IsNullable = false)]
        public string PasswordHash { get; set; } = string.Empty;

        public UserRole Role { get; set; } = UserRole.Admin;
        public bool Enabled { get; set; } = true;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginUtc { get; set; }
    }
}