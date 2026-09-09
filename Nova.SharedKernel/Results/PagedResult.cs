namespace Nova.SharedKernel.Results;

/// <summary>
/// Represents a paged response with items and paging metadata.
/// </summary>
/// <typeparam name="TItem">The item type in the page.</typeparam>
/// <param name="Items">The items in the current page.</param>
/// <param name="Page">The 1-based page number returned.</param>
/// <param name="PageSize">The number of items requested per page.</param>
/// <param name="TotalCount">The total number of items matching the query before paging.</param>
public sealed record PagedResult<TItem>(
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<TItem> Items,
    [property: System.Text.Json.Serialization.JsonRequired] int Page,
    [property: System.Text.Json.Serialization.JsonRequired] int PageSize,
    [property: System.Text.Json.Serialization.JsonRequired] int TotalCount);
