namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Defines shared route constants for campaign command and query endpoints.
/// </summary>
public static class CampaignEndpoints
{
    /// <summary>
    /// The group prefix for campaign endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/campaigns";

    /// <summary>
    /// Creates an Active campaign and its initial participation snapshot (POST).
    /// </summary>
    public const string Create = GroupPrefix;

    /// <summary>
    /// The relative creation path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string CreateRelative = "";

    /// <summary>
    /// The route name assigned to campaign creation.
    /// </summary>
    public const string CreateRouteName = "CreateCampaign";

    /// <summary>
    /// Gets the campaign-list route.
    /// </summary>
    public const string GetCampaignList = GroupPrefix;

    /// <summary>
    /// Gets the campaign-list route relative to the campaign group.
    /// </summary>
    public const string GetCampaignListRelative = "";

    /// <summary>
    /// Gets the route name assigned to the campaign list.
    /// </summary>
    public const string GetCampaignListRouteName = "GetCampaignList";

    /// <summary>
    /// Gets the campaign creation-setup route.
    /// </summary>
    public const string GetCreationSetup = $"{GroupPrefix}/creation-setup";

    /// <summary>
    /// Gets the creation-setup route relative to the campaign group.
    /// </summary>
    public const string GetCreationSetupRelative = "creation-setup";

    /// <summary>
    /// Gets the route name assigned to campaign creation setup.
    /// </summary>
    public const string GetCreationSetupRouteName = "GetCampaignCreationSetup";

    /// <summary>
    /// Gets the campaign-detail route.
    /// </summary>
    public const string GetCampaignDetail = $"{GroupPrefix}/{{campaignId:long}}";

    /// <summary>
    /// Gets the campaign-detail route relative to the campaign group.
    /// </summary>
    public const string GetCampaignDetailRelative = "{campaignId:long}";

    /// <summary>
    /// Gets the route name assigned to the campaign detail.
    /// </summary>
    public const string GetCampaignDetailRouteName = "GetCampaignDetail";

    /// <summary>
    /// Gets the campaign-participant roster route.
    /// </summary>
    public const string GetCampaignParticipantRoster = $"{GroupPrefix}/{{campaignId:long}}/participants";

    /// <summary>
    /// Gets the campaign-participant roster route relative to the campaign group.
    /// </summary>
    public const string GetCampaignParticipantRosterRelative = "{campaignId:long}/participants";

    /// <summary>
    /// Gets the route name assigned to the campaign participant roster.
    /// </summary>
    public const string GetCampaignParticipantRosterRouteName = "GetCampaignParticipantRoster";

    /// <summary>
    /// Gets the campaign-participant detail route.
    /// </summary>
    public const string GetCampaignParticipantDetail = $"{GroupPrefix}/{{campaignId:long}}/participants/{{playerCampaignAssignmentId:long}}";

    /// <summary>
    /// Gets the campaign-participant detail route relative to the campaign group.
    /// </summary>
    public const string GetCampaignParticipantDetailRelative = "{campaignId:long}/participants/{playerCampaignAssignmentId:long}";

    /// <summary>
    /// Gets the route name assigned to the campaign participant detail.
    /// </summary>
    public const string GetCampaignParticipantDetailRouteName = "GetCampaignParticipantDetail";

    /// <summary>
    /// Gets the campaign-participant graduation-years route.
    /// </summary>
    public const string GetCampaignParticipantGraduationYears = $"{GroupPrefix}/{{campaignId:long}}/participants/graduation-years";

    /// <summary>
    /// Gets the campaign-participant graduation-years route relative to the campaign group.
    /// </summary>
    public const string GetCampaignParticipantGraduationYearsRelative = "{campaignId:long}/participants/graduation-years";

    /// <summary>
    /// Gets the route name assigned to the campaign participant graduation years.
    /// </summary>
    public const string GetCampaignParticipantGraduationYearsRouteName = "GetCampaignParticipantGraduationYears";

    /// <summary>
    /// Gets the campaign placement roster route.
    /// </summary>
    public const string GetCampaignPlacementRoster = $"{GroupPrefix}/{{campaignId:long}}/placements";

    /// <summary>
    /// Gets the campaign placement roster route relative to the campaign group.
    /// </summary>
    public const string GetCampaignPlacementRosterRelative = "{campaignId:long}/placements";

    /// <summary>
    /// Gets the route name assigned to the campaign placement roster.
    /// </summary>
    public const string GetCampaignPlacementRosterRouteName = "GetCampaignPlacementRoster";

    /// <summary>
    /// Gets the campaign placement summary route.
    /// </summary>
    public const string GetCampaignPlacementSummary = $"{GroupPrefix}/{{campaignId:long}}/placements/summary";

    /// <summary>
    /// Gets the campaign placement summary route relative to the campaign group.
    /// </summary>
    public const string GetCampaignPlacementSummaryRelative = "{campaignId:long}/placements/summary";

    /// <summary>
    /// Gets the route name assigned to the campaign placement summary.
    /// </summary>
    public const string GetCampaignPlacementSummaryRouteName = "GetCampaignPlacementSummary";

    /// <summary>
    /// Applies a tag definition to a campaign participation (POST).
    /// </summary>
    public const string ApplyCampaignTagApplication = $"{GroupPrefix}/tag-applications";

    /// <summary>
    /// The relative apply path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string ApplyCampaignTagApplicationRelative = "tag-applications";

    /// <summary>
    /// The route name assigned to campaign tag application.
    /// </summary>
    public const string ApplyCampaignTagApplicationRouteName = "ApplyCampaignTagApplication";

    /// <summary>
    /// Removes a campaign tag application (DELETE).
    /// </summary>
    public const string RemoveCampaignTagApplication = $"{GroupPrefix}/tag-applications/{{campaignTagApplicationId:long}}";

    /// <summary>
    /// The relative remove path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string RemoveCampaignTagApplicationRelative = "tag-applications/{campaignTagApplicationId:long}";

    /// <summary>
    /// The route name assigned to campaign tag application removal.
    /// </summary>
    public const string RemoveCampaignTagApplicationRouteName = "RemoveCampaignTagApplication";

    /// <summary>
    /// Updates an Active campaign's metadata (PUT).
    /// </summary>
    public const string UpdateCampaignMetadata = $"{GroupPrefix}/metadata";

    /// <summary>
    /// The relative metadata-update path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string UpdateCampaignMetadataRelative = "metadata";

    /// <summary>
    /// Adds an evaluation note to a campaign participation (POST).
    /// </summary>
    public const string AddEvaluationNote = $"{GroupPrefix}/evaluation-notes";

    /// <summary>
    /// The relative add path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string AddEvaluationNoteRelative = "evaluation-notes";

    /// <summary>
    /// The route name assigned to evaluation note addition.
    /// </summary>
    public const string AddEvaluationNoteRouteName = "AddEvaluationNote";

    /// <summary>
    /// Edits an evaluation note (PUT).
    /// </summary>
    public const string EditEvaluationNoteTemplate = $"{GroupPrefix}/evaluation-notes/{{noteId:long}}";

    /// <summary>
    /// The relative edit path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string EditEvaluationNoteRelative = "evaluation-notes/{noteId:long}";

    /// <summary>
    /// The route name assigned to evaluation note editing.
    /// </summary>
    public const string EditEvaluationNoteRouteName = "EditEvaluationNote";

    /// <summary>
    /// Deletes an evaluation note (DELETE).
    /// </summary>
    public const string DeleteEvaluationNoteTemplate = $"{GroupPrefix}/evaluation-notes/{{noteId:long}}";

    /// <summary>
    /// The relative delete path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string DeleteEvaluationNoteRelative = "evaluation-notes/{noteId:long}";

    /// <summary>
    /// The route name assigned to evaluation note deletion.
    /// </summary>
    public const string DeleteEvaluationNoteRouteName = "DeleteEvaluationNote";

    /// <summary>
    /// Updates a season's metadata (PUT).
    /// </summary>
    public const string UpdateSeasonMetadata = $"{GroupPrefix}/seasons/metadata";

    /// <summary>
    /// The relative season metadata-update path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string UpdateSeasonMetadataRelative = "seasons/metadata";

    /// <summary>
    /// Builds a campaign-list URL from the accepted optional filters.
    /// </summary>
    /// <param name="status">The optional campaign status filter.</param>
    /// <param name="limit">The optional bounded result limit.</param>
    /// <returns>The campaign-list URL.</returns>
    public static string GetCampaignListUrl(string? status = null, int? limit = null)
    {
        var querySegments = new List<string>();
        var normalizedStatus = status?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "closed" => "closed",
            _ => null
        };

        if (normalizedStatus is not null)
        {
            querySegments.Add($"status={Uri.EscapeDataString(normalizedStatus)}");
        }

        if (limit is >= GetCampaignListInput.MinLimit and <= GetCampaignListInput.MaxLimit)
        {
            querySegments.Add($"limit={limit.Value}");
        }

        return querySegments.Count == 0
            ? GetCampaignList
            : $"{GetCampaignList}?{string.Join('&', querySegments)}";
    }

    /// <summary>
    /// Builds a campaign-detail URL.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <returns>The detail URL.</returns>
    public static string GetCampaignDetailUrl(long campaignId)
        => $"{GroupPrefix}/{campaignId}";

    /// <summary>
    /// Builds a campaign-participant roster URL from the accepted optional filters.
    /// </summary>
    /// <param name="input">The roster query input.</param>
    /// <returns>The roster URL.</returns>
    public static string GetCampaignParticipantRosterUrl(GetCampaignParticipantRosterInput input)
    {
        var querySegments = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.Search))
        {
            querySegments.Add($"search={Uri.EscapeDataString(input.Search.Trim())}");
        }

        if (input.GraduationYears is { Length: > 0 })
        {
            foreach (var graduationYear in input.GraduationYears.Distinct())
            {
                querySegments.Add($"graduationYears={graduationYear}");
            }
        }

        if (input.TagDefinitionIds is { Length: > 0 })
        {
            foreach (var tagDefinitionId in input.TagDefinitionIds.Distinct())
            {
                querySegments.Add($"tagDefinitionIds={tagDefinitionId}");
            }
        }

        var normalizedOutcome = input.Outcome?.Trim().ToLowerInvariant() switch
        {
            "undecided" => "undecided",
            "assigned" => "assigned",
            "notselected" => "notselected",
            "withdrawn" => "withdrawn",
            _ => null
        };

        if (normalizedOutcome is not null)
        {
            querySegments.Add($"outcome={normalizedOutcome}");
        }

        if (input.TeamId is > 0)
        {
            querySegments.Add($"teamId={input.TeamId.Value}");
        }

        var normalizedSortBy = input.SortBy?.Trim().ToLowerInvariant() switch
        {
            "displayname" => "displayName",
            "graduationyear" => "graduationYear",
            "tryoutnumber" => "tryoutNumber",
            "assignmentid" => "assignmentId",
            "outcome" => "outcome",
            "teamname" => "teamName",
            _ => null
        };

        if (normalizedSortBy is not null)
        {
            querySegments.Add($"sortBy={normalizedSortBy}");
        }

        var normalizedSortDirection = input.SortDirection?.Trim().ToLowerInvariant() switch
        {
            "asc" => "asc",
            "desc" => "desc",
            _ => null
        };

        if (normalizedSortDirection is not null)
        {
            querySegments.Add($"sortDirection={normalizedSortDirection}");
        }

        if (input.Page is > 0)
        {
            querySegments.Add($"page={input.Page.Value}");
        }

        if (input.PageSize is > 0)
        {
            querySegments.Add($"pageSize={input.PageSize.Value}");
        }

        var baseUrl = $"{GroupPrefix}/{input.CampaignId}/participants";
        return querySegments.Count == 0
            ? baseUrl
            : $"{baseUrl}?{string.Join('&', querySegments)}";
    }

    /// <summary>
    /// Builds a campaign-participant detail URL.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="playerCampaignAssignmentId">The participant assignment identifier.</param>
    /// <returns>The detail URL.</returns>
    public static string GetCampaignParticipantDetailUrl(long campaignId, long playerCampaignAssignmentId)
        => $"{GroupPrefix}/{campaignId}/participants/{playerCampaignAssignmentId}";

    /// <summary>
    /// Builds a campaign-participant graduation-years URL.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <returns>The graduation-years URL.</returns>
    public static string GetCampaignParticipantGraduationYearsUrl(long campaignId)
        => $"{GroupPrefix}/{campaignId}/participants/graduation-years";

    /// <summary>
    /// Builds a campaign placement roster URL from the accepted optional filters.
    /// </summary>
    /// <param name="input">The placement roster query input.</param>
    /// <returns>The placement roster URL.</returns>
    public static string GetCampaignPlacementRosterUrl(GetCampaignPlacementRosterInput input)
    {
        var querySegments = new List<string>();
        if (input.GraduationYear is > 0)
        {
            querySegments.Add($"graduationYear={input.GraduationYear.Value}");
        }

        if (input.UnresolvedOnly == true)
        {
            querySegments.Add("unresolvedOnly=true");
        }

        if (input.Page is > 0)
        {
            querySegments.Add($"page={input.Page.Value}");
        }

        if (input.PageSize is > 0)
        {
            querySegments.Add($"pageSize={input.PageSize.Value}");
        }

        var baseUrl = $"{GroupPrefix}/{input.CampaignId}/placements";
        return querySegments.Count == 0
            ? baseUrl
            : $"{baseUrl}?{string.Join('&', querySegments)}";
    }

    /// <summary>
    /// Builds a campaign placement summary URL.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <returns>The placement summary URL.</returns>
    public static string GetCampaignPlacementSummaryUrl(long campaignId)
        => $"{GroupPrefix}/{campaignId}/placements/summary";

    /// <summary>
    /// Builds a campaign tag application removal URL.
    /// </summary>
    /// <param name="campaignTagApplicationId">The campaign tag application identifier.</param>
    /// <returns>The removal URL.</returns>
    public static string RemoveCampaignTagApplicationUrl(long campaignTagApplicationId)
        => $"{GroupPrefix}/tag-applications/{campaignTagApplicationId}";

    /// <summary>
    /// Builds an evaluation note edit URL.
    /// </summary>
    /// <param name="noteId">The evaluation note identifier.</param>
    /// <returns>The edit URL.</returns>
    public static string EditEvaluationNoteUrl(long noteId)
        => $"{GroupPrefix}/evaluation-notes/{noteId}";

    /// <summary>
    /// Builds an evaluation note deletion URL.
    /// </summary>
    /// <param name="noteId">The evaluation note identifier.</param>
    /// <returns>The deletion URL.</returns>
    public static string DeleteEvaluationNoteUrl(long noteId)
        => $"{GroupPrefix}/evaluation-notes/{noteId}";
}
