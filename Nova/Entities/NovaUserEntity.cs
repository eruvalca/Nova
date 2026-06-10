using Microsoft.AspNetCore.Identity;

namespace Nova.Entities;

/// <summary>
/// Represents the Nova User Entity persisted in the database.
/// </summary>
public class NovaUserEntity : IdentityUser<long>
{
    /// <summary>
    /// Gets or sets the First Name.
    /// </summary>
    public required string FirstName { get; set; }
    /// <summary>
    /// Gets or sets the Last Name.
    /// </summary>
    public required string LastName { get; set; }
    /// <summary>
    /// Gets the Full Name.
    /// </summary>
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public long? ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity? Club { get; set; }
    /// <summary>
    /// Gets or sets the Sent Join Request.
    /// </summary>
    public ClubJoinRequestEntity? SentJoinRequest { get; set; }
    /// <summary>
    /// Gets or sets the Photos.
    /// </summary>
    public ICollection<NovaUserPhotoEntity> Photos { get; set; } = [];
}
