namespace ArchiveSystem.Core.Models
{
    public class Location
    {
        public int LocationId { get; set; }
        public int HallwayNumber { get; set; }
        public int CabinetNumber { get; set; }
        public int ShelfNumber { get; set; }
        public string? Label { get; set; }
        public int? Capacity { get; set; }
        public bool IsActive { get; set; } = true;
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }

        // display helper — not stored in DB
        public string DisplayName =>
            $"ممر {HallwayNumber} - كبينة {CabinetNumber} - رف {ShelfNumber}";
    }
}