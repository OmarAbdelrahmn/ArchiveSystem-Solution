using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class LocationService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        public List<Location> GetAllLocations()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Location>(@"
                SELECT l.*,
                       COUNT(d.DossierId) AS DossierCount
                FROM Locations l
                LEFT JOIN Dossiers d ON d.CurrentLocationId = l.LocationId
                GROUP BY l.LocationId
                ORDER BY l.HallwayNumber, l.CabinetNumber, l.ShelfNumber").AsList();
        }

        public List<Location> GetActiveLocations()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Location>(@"
                SELECT * FROM Locations WHERE IsActive = 1
                ORDER BY HallwayNumber, CabinetNumber, ShelfNumber").AsList();
        }

        public Location? GetById(int locationId)
        {
            using var conn = _db.CreateConnection();
            return conn.QuerySingleOrDefault<Location>(
                "SELECT * FROM Locations WHERE LocationId = @Id",
                new { Id = locationId });
        }

        public string? CreateLocation(int hallway, int cabinet, int shelf,
            string? label = null, int? capacity = null)
        {
            if (hallway <= 0 || cabinet <= 0 || shelf <= 0)
                return "يجب أن تكون أرقام الممر والكبينة والرف أكبر من صفر.";

            using var conn = _db.CreateConnection();
            int exists = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Locations
                WHERE HallwayNumber = @H AND CabinetNumber = @C AND ShelfNumber = @S",
                new { H = hallway, C = cabinet, S = shelf });

            if (exists > 0) return "هذا الموقع موجود مسبقاً.";

            conn.Execute(@"
                INSERT INTO Locations
                    (HallwayNumber, CabinetNumber, ShelfNumber,
                     Label, Capacity, IsActive, CreatedAt)
                VALUES (@H, @C, @S, @Label, @Capacity, 1, @Now)",
                new
                {
                    H = hallway,
                    C = cabinet,
                    S = shelf,
                    Label = string.IsNullOrWhiteSpace(label) ? (object)DBNull.Value : label.Trim(),
                    Capacity = capacity.HasValue ? (object)capacity.Value : DBNull.Value,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });

            return null;
        }

        public string? UpdateLocation(int locationId, string? label,
            int? capacity, bool isActive)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE Locations
                SET Label     = @Label,
                    Capacity  = @Capacity,
                    IsActive  = @IsActive,
                    UpdatedAt = @Now
                WHERE LocationId = @Id",
                new
                {
                    Label = string.IsNullOrWhiteSpace(label) ? (object)DBNull.Value : label.Trim(),
                    Capacity = capacity.HasValue ? (object)capacity.Value : DBNull.Value,
                    IsActive = isActive ? 1 : 0,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Id = locationId
                });
            return null;
        }

        public bool LocationExists(int hallway, int cabinet, int shelf)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Locations
                WHERE HallwayNumber = @H AND CabinetNumber = @C AND ShelfNumber = @S",
                new { H = hallway, C = cabinet, S = shelf }) > 0;
        }

        public List<int> GetDistinctHallways()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<int>(
                "SELECT DISTINCT HallwayNumber FROM Locations WHERE IsActive=1 ORDER BY HallwayNumber")
                .AsList();
        }
    }
}