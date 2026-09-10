SELECT s6."Key", count(*)::int
FROM (
    SELECT CASE
        WHEN p1."LifecycleStatus" <> 0 OR (s5."PlayerCampaignAssignmentId" IS NOT NULL AND s5."PlacementOutcome" = 3) THEN 3
        WHEN s5."PlayerCampaignAssignmentId" IS NOT NULL AND s5."PlacementOutcome" = 1 AND t0."TeamId" IS NOT NULL AND t0."ClubId" = @clubId AND t0."LifecycleStatus" = 0 AND p1."GraduationYear" >= t0."GraduationYear" THEN 1
        WHEN s5."PlayerCampaignAssignmentId" IS NOT NULL AND s5."PlacementOutcome" = 2 AND s5."CampaignId" = p."CampaignId" THEN 2
        ELSE 0
    END AS "Key"
    FROM "PlayerCampaignAssignments" AS p
    INNER JOIN (
        SELECT c."CampaignId", c."ClubId", c."SeasonId", c."Status"
        FROM "Campaigns" AS c
        WHERE @ef_filter___bypassTenantFilter4 OR (c."ClubId" = @ef_filter__ClubId AND (@ef_filter__IsClubAdmin3 OR c."Status" <> 2))
    ) AS c0 ON p."CampaignId" = c0."CampaignId"
    INNER JOIN (
        SELECT p0."PlayerId", p0."ClubId", p0."GraduationYear", p0."LifecycleStatus"
        FROM "Players" AS p0
        WHERE @ef_filter___bypassTenantFilter12 OR p0."ClubId" = @ef_filter__ClubId11
    ) AS p1 ON p."PlayerId" = p1."PlayerId"
    INNER JOIN (
        SELECT s."SeasonId", s."ClubId"
        FROM "Seasons" AS s
        WHERE @ef_filter___bypassTenantFilter12 OR s."ClubId" = @ef_filter__ClubId11
    ) AS s0 ON c0."SeasonId" = s0."SeasonId" AND c0."ClubId" = s0."ClubId"
    INNER JOIN "Clubs" AS c1 ON c0."ClubId" = c1."ClubId"
    LEFT JOIN (
        SELECT p2."PlayerCampaignAssignmentId", p2."CampaignId", p2."PlacementOutcome", p2."TeamId"
        FROM "PlayerCampaignAssignments" AS p2
        INNER JOIN (
            SELECT c2."CampaignId", c2."ClubId", c2."SeasonId", c2."SeasonOpeningSequence", c2."Status"
            FROM "Campaigns" AS c2
            WHERE @ef_filter___bypassTenantFilter4 OR (c2."ClubId" = @ef_filter__ClubId AND (@ef_filter__IsClubAdmin3 OR c2."Status" <> 2))
        ) AS c3 ON p2."CampaignId" = c3."CampaignId"
        INNER JOIN (
            SELECT p3."PlayerId", p3."ClubId"
            FROM "Players" AS p3
            WHERE @ef_filter___bypassTenantFilter12 OR p3."ClubId" = @ef_filter__ClubId11
        ) AS p4 ON p2."PlayerId" = p4."PlayerId"
        INNER JOIN (
            SELECT s1."SeasonId", s1."ClubId"
            FROM "Seasons" AS s1
            WHERE @ef_filter___bypassTenantFilter12 OR s1."ClubId" = @ef_filter__ClubId11
        ) AS s2 ON c3."SeasonId" = s2."SeasonId" AND c3."ClubId" = s2."ClubId"
        INNER JOIN "Clubs" AS c4 ON c3."ClubId" = c4."ClubId"
        WHERE (@ef_filter___bypassTenantFilter4 OR (p2."ClubId" = @ef_filter__ClubId AND (@ef_filter__IsClubAdmin3 OR c3."Status" <> 2))) AND p2."ClubId" = @clubId4 AND p4."ClubId" = @clubId4 AND c3."ClubId" = @clubId4 AND s2."ClubId" = @clubId4 AND c3."SeasonId" = c4."CurrentSeasonId" AND c3."Status" IN (0, 1) AND c3."SeasonOpeningSequence" IS NOT NULL AND p2."PlacementOutcome" <> 0
    ) AS s5 ON (
        SELECT p5."PlayerCampaignAssignmentId"
        FROM "PlayerCampaignAssignments" AS p5
        INNER JOIN (
            SELECT c5."CampaignId", c5."ClubId", c5."SeasonId", c5."SeasonOpeningSequence", c5."Status"
            FROM "Campaigns" AS c5
            WHERE @ef_filter___bypassTenantFilter4 OR (c5."ClubId" = @ef_filter__ClubId AND (@ef_filter__IsClubAdmin3 OR c5."Status" <> 2))
        ) AS c6 ON p5."CampaignId" = c6."CampaignId"
        INNER JOIN (
            SELECT p6."PlayerId", p6."ClubId"
            FROM "Players" AS p6
            WHERE @ef_filter___bypassTenantFilter12 OR p6."ClubId" = @ef_filter__ClubId11
        ) AS p7 ON p5."PlayerId" = p7."PlayerId"
        INNER JOIN (
            SELECT s3."SeasonId", s3."ClubId"
            FROM "Seasons" AS s3
            WHERE @ef_filter___bypassTenantFilter12 OR s3."ClubId" = @ef_filter__ClubId11
        ) AS s4 ON c6."SeasonId" = s4."SeasonId" AND c6."ClubId" = s4."ClubId"
        INNER JOIN "Clubs" AS c7 ON c6."ClubId" = c7."ClubId"
        WHERE (@ef_filter___bypassTenantFilter4 OR (p5."ClubId" = @ef_filter__ClubId AND (@ef_filter__IsClubAdmin3 OR c6."Status" <> 2))) AND p5."ClubId" = @clubId4 AND p7."ClubId" = @clubId4 AND c6."ClubId" = @clubId4 AND s4."ClubId" = @clubId4 AND c6."SeasonId" = c7."CurrentSeasonId" AND c6."Status" IN (0, 1) AND c6."SeasonOpeningSequence" IS NOT NULL AND p5."PlacementOutcome" <> 0 AND p5."PlayerId" = p."PlayerId"
        ORDER BY c6."SeasonOpeningSequence" DESC, p5."PlayerCampaignAssignmentId" DESC
        LIMIT 1) = s5."PlayerCampaignAssignmentId"
    LEFT JOIN (
        SELECT t."TeamId", t."ClubId", t."GraduationYear", t."LifecycleStatus"
        FROM "Teams" AS t
        WHERE @ef_filter___bypassTenantFilter12 OR t."ClubId" = @ef_filter__ClubId11
    ) AS t0 ON s5."TeamId" = t0."TeamId"
    WHERE (@ef_filter___bypassTenantFilter4 OR (p."ClubId" = @ef_filter__ClubId AND (@ef_filter__IsClubAdmin3 OR c0."Status" <> 2))) AND p."ClubId" = @clubId AND p1."ClubId" = @clubId AND c0."ClubId" = @clubId AND s0."ClubId" = @clubId AND c0."Status" = 0 AND c0."SeasonId" = c1."CurrentSeasonId" AND p."CampaignId" = @input_CampaignId
) AS s6
GROUP BY s6."Key"
