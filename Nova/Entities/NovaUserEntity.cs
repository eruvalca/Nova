#pragma warning disable CA1515 // Identity components expose these framework model and navigation types through their public constructors.
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
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<NovaUserPhotoEntity> Photos { get; set; } = [];
#pragma warning restore CA2227
}
