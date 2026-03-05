namespace EliteBridgePlanner.Server.Models;

public class Bridge
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }

    public ICollection<StarSystem> Systems { get; set; } = [];
}
