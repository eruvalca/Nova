using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Entities.Base;

namespace Nova.Data;

/// <summary>
/// Abstract base context defining the full application model (Identity + domain entities)
/// and the tenant global query filters. Do not use directly; use <see cref="NovaDbContext"/>,
/// <see cref="NovaReadDbContext"/>, or <see cref="NovaAdminDbContext"/>.
/// </summary>
public abstract class ApplicationDbContext : IdentityDbContext<NovaUserEntity, IdentityRole<long>, long>
{
    /// <summary>
    /// The provider exposing the current user's id, club id, and roles.
    /// Referenced by query filter expressions so EF parameterizes them per context instance.
    /// </summary>
    protected readonly ICurrentUserProvider _currentUser;

    /// <summary>
    /// When true, all tenant query filters are bypassed. Set by <see cref="NovaAdminDbContext"/>.
    /// Referenced by query filter expressions so EF parameterizes them per context instance.
    /// </summary>
    protected readonly bool _bypassTenantFilter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="currentUser">The current user provider.</param>
    /// <param name="bypassTenantFilter">Whether tenant query filters are bypassed.</param>
    protected ApplicationDbContext(DbContextOptions options, ICurrentUserProvider currentUser, bool bypassTenantFilter)
        : base(options)
    {
        _currentUser = currentUser;
        _bypassTenantFilter = bypassTenantFilter;
    }

    /// <summary>
    /// Gets or sets the Clubs.
    /// </summary>
    public DbSet<ClubEntity> Clubs => Set<ClubEntity>();
    /// <summary>
    /// Gets the Club Join Requests.
    /// </summary>
    public DbSet<ClubJoinRequestEntity> ClubJoinRequests => Set<ClubJoinRequestEntity>();
    /// <summary>
    /// Gets the Seasons.
    /// </summary>
    public DbSet<SeasonEntity> Seasons => Set<SeasonEntity>();
    /// <summary>
    /// Gets the Campaigns.
    /// </summary>
    public DbSet<CampaignEntity> Campaigns => Set<CampaignEntity>();
    /// <summary>
    /// Gets the Teams.
    /// </summary>
    public DbSet<TeamEntity> Teams => Set<TeamEntity>();
    /// <summary>
    /// Gets the Players.
    /// </summary>
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    /// <summary>
    /// Gets the Player Tags.
    /// </summary>
    public DbSet<PlayerTagEntity> PlayerTags => Set<PlayerTagEntity>();
    /// <summary>
    /// Gets the Notes.
    /// </summary>
    public DbSet<NoteEntity> Notes => Set<NoteEntity>();
    /// <summary>
    /// Gets the Player Photos.
    /// </summary>
    public DbSet<PlayerPhotoEntity> PlayerPhotos => Set<PlayerPhotoEntity>();
    /// <summary>
    /// Gets the Player Campaign Assignments.
    /// </summary>
    public DbSet<PlayerCampaignAssignmentEntity> PlayerCampaignAssignments => Set<PlayerCampaignAssignmentEntity>();
    /// <summary>
    /// Gets the Campaign Lifecycle Events.
    /// </summary>
    public DbSet<CampaignLifecycleEventEntity> CampaignLifecycleEvents => Set<CampaignLifecycleEventEntity>();
    /// <summary>
    /// Gets the Nova User Photos.
    /// </summary>
    public DbSet<NovaUserPhotoEntity> NovaUserPhotos => Set<NovaUserPhotoEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Generic tenant filter for all ITenantOwnedEntity types:
        //   e => _bypassTenantFilter || e.ClubId == _currentUser.ClubId
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwnedEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(BuildTenantFilter(entityType.ClrType));
            }
        }

        // Bespoke filters (HasQueryFilter replaces any previous filter, so these win).
        // ClubEntity intentionally has NO filter: users must browse clubs to request joining.

        // Join requests: visible to the requester, and to ClubAdmins of the target club.
        modelBuilder.Entity<ClubJoinRequestEntity>().HasQueryFilter(e =>
            _bypassTenantFilter
            || e.RequestingUserId == _currentUser.UserId
            || (_currentUser.IsClubAdmin && e.ClubId == _currentUser.ClubId));

        // Users: visible to fellow club members; a user can always see themselves.
        modelBuilder.Entity<NovaUserEntity>().HasQueryFilter(e =>
            _bypassTenantFilter
            || (e.ClubId != null && e.ClubId == _currentUser.ClubId)
            || e.Id == _currentUser.UserId);

        // User photos: mirror the user filter (required dependent of a filtered principal).
        modelBuilder.Entity<NovaUserPhotoEntity>().HasQueryFilter(e =>
            _bypassTenantFilter
            || (e.NovaUser.ClubId != null && e.NovaUser.ClubId == _currentUser.ClubId)
            || e.NovaUserId == _currentUser.UserId);
    }

    private LambdaExpression BuildTenantFilter(Type entityClrType)
    {
        // e => _bypassTenantFilter || (long?)e.ClubId == _currentUser.ClubId
        var parameter = Expression.Parameter(entityClrType, "e");
        var self = Expression.Constant(this);

        var bypass = Expression.Field(self, nameof(_bypassTenantFilter));
        var clubId = Expression.Convert(
            Expression.Property(parameter, nameof(ITenantOwnedEntity.ClubId)),
            typeof(long?));
        var tenantId = Expression.Property(
            Expression.Field(self, nameof(_currentUser)),
            nameof(ICurrentUserProvider.ClubId));

        var body = Expression.OrElse(bypass, Expression.Equal(clubId, tenantId));
        return Expression.Lambda(body, parameter);
    }

    /// <summary>
    /// Gets the current user provider (for interceptors).
    /// </summary>
    internal ICurrentUserProvider CurrentUser => _currentUser;

    /// <summary>
    /// Gets a value indicating whether tenant filters are bypassed (for interceptors).
    /// </summary>
    internal bool TenantFilterBypassed => _bypassTenantFilter;
}
