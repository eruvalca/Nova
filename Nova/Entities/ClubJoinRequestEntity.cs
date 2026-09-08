#pragma warning disable CA1515 // Identity components expose these framework model and navigation types through their public constructors.
using Nova.Entities.Base;
using Nova.SharedKernel.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents the Club Join Request Entity persisted in the database.
/// </summary>
public class ClubJoinRequestEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the Club Join Request Id.
    /// </summary>
    public long ClubJoinRequestId { get; set; }

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Requesting User Id.
    /// </summary>
    public required long RequestingUserId { get; set; }
    /// <summary>
    /// Gets or sets the Requesting User.
    /// </summary>
    public NovaUserEntity RequestingUser { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Status.
    /// </summary>
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
}
