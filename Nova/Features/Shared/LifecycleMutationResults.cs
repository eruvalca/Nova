namespace Nova.Features.Shared;

/// <summary>
/// Reports that the current user is not authorized to mutate a record's lifecycle.
/// </summary>
/// <param name="Detail">A description of the authorization failure.</param>
public readonly record struct LifecycleForbidden(string Detail);

/// <summary>
/// Reports that a lifecycle or permanent-data mutation conflicts with the record's current state.
/// </summary>
/// <param name="Detail">A description of the conflicting state or related records.</param>
public readonly record struct LifecycleConflict(string Detail);
