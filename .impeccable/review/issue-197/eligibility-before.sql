EXPLAIN (ANALYZE, BUFFERS, TIMING OFF, FORMAT JSON) SELECT s6."Key", count(*)::int
FROM (
    SELECT CASE
        WHEN p1."LifecycleStatus" <> 0 OR (s5."PlayerCampaignAssignmentId" IS NOT NULL AND s5."PlacementOutcome" = 3) THEN 3
        WHEN s5."PlayerCampaignAssignmentId" IS NOT NULL AND s5."PlacementOutcome" = 1 AND t0."TeamId" IS NOT NULL AND t0."ClubId" = 2 AND t0."LifecycleStatus" = 0 AND p1."GraduationYear" >= t0."GraduationYear" THEN 1
        WHEN s5."PlayerCampaignAssignmentId" IS NOT NULL AND s5."PlacementOutcome" = 2 AND s5."CampaignId" = p."CampaignId" THEN 2
        ELSE 0
    END AS "Key"
    FROM "PlayerCampaignAssignments" AS p
    INNER JOIN (
        SELECT c."CampaignId", c."ClubId", c."SeasonId", c."Status"
        FROM "Campaigns" AS c
        WHERE FALSE OR (c."ClubId" = 2 AND (FALSE OR c."Status" <> 2))
    ) AS c0 ON p."CampaignId" = c0."CampaignId"
    INNER JOIN (
        SELECT p0."PlayerId", p0."ClubId", p0."GraduationYear", p0."LifecycleStatus"
        FROM "Players" AS p0
        WHERE FALSE OR p0."ClubId" = 2
    ) AS p1 ON p."PlayerId" = p1."PlayerId"
    INNER JOIN (
        SELECT s."SeasonId", s."ClubId"
        FROM "Seasons" AS s
        WHERE FALSE OR s."ClubId" = 2
    ) AS s0 ON c0."SeasonId" = s0."SeasonId" AND c0."ClubId" = s0."ClubId"
    INNER JOIN "Clubs" AS c1 ON c0."ClubId" = c1."ClubId"
    LEFT JOIN (
        SELECT p2."PlayerCampaignAssignmentId", p2."CampaignId", p2."PlacementOutcome", p2."PlayerId", p2."TeamId"
        FROM "PlayerCampaignAssignments" AS p2
        INNER JOIN (
            SELECT c2."CampaignId", c2."ClubId", c2."SeasonId", c2."SeasonOpeningSequence", c2."Status"
            FROM "Campaigns" AS c2
            WHERE FALSE OR (c2."ClubId" = 2 AND (FALSE OR c2."Status" <> 2))
        ) AS c3 ON p2."CampaignId" = c3."CampaignId"
        INNER JOIN (
            SELECT p3."PlayerId", p3."ClubId"
            FROM "Players" AS p3
            WHERE FALSE OR p3."ClubId" = 2
        ) AS p4 ON p2."PlayerId" = p4."PlayerId"
        INNER JOIN (
            SELECT s1."SeasonId", s1."ClubId"
            FROM "Seasons" AS s1
            WHERE FALSE OR s1."ClubId" = 2
        ) AS s2 ON c3."SeasonId" = s2."SeasonId" AND c3."ClubId" = s2."ClubId"
        INNER JOIN "Clubs" AS c4 ON c3."ClubId" = c4."ClubId"
        WHERE (FALSE OR (p2."ClubId" = 2 AND (FALSE OR c3."Status" <> 2))) AND p2."ClubId" = 2 AND p4."ClubId" = 2 AND c3."ClubId" = 2 AND s2."ClubId" = 2 AND c3."SeasonId" = c4."CurrentSeasonId" AND c3."Status" IN (0, 1) AND c3."SeasonOpeningSequence" IS NOT NULL AND p2."PlacementOutcome" <> 0 AND NOT EXISTS (
            SELECT 1
            FROM "PlayerCampaignAssignments" AS p5
            INNER JOIN (
                SELECT c5."CampaignId", c5."ClubId", c5."SeasonId", c5."SeasonOpeningSequence", c5."Status"
                FROM "Campaigns" AS c5
                WHERE FALSE OR (c5."ClubId" = 2 AND (FALSE OR c5."Status" <> 2))
            ) AS c6 ON p5."CampaignId" = c6."CampaignId"
            INNER JOIN (
                SELECT p6."PlayerId", p6."ClubId"
                FROM "Players" AS p6
                WHERE FALSE OR p6."ClubId" = 2
            ) AS p7 ON p5."PlayerId" = p7."PlayerId"
            INNER JOIN (
                SELECT s3."SeasonId", s3."ClubId"
                FROM "Seasons" AS s3
                WHERE FALSE OR s3."ClubId" = 2
            ) AS s4 ON c6."SeasonId" = s4."SeasonId" AND c6."ClubId" = s4."ClubId"
            INNER JOIN "Clubs" AS c7 ON c6."ClubId" = c7."ClubId"
            WHERE (FALSE OR (p5."ClubId" = 2 AND (FALSE OR c6."Status" <> 2))) AND p5."ClubId" = 2 AND p7."ClubId" = 2 AND c6."ClubId" = 2 AND s4."ClubId" = 2 AND c6."SeasonId" = c7."CurrentSeasonId" AND c6."Status" IN (0, 1) AND c6."SeasonOpeningSequence" IS NOT NULL AND p5."PlacementOutcome" <> 0 AND p5."PlayerId" = p2."PlayerId" AND c6."SeasonId" = c3."SeasonId" AND (c6."SeasonOpeningSequence" > c3."SeasonOpeningSequence" OR (c6."SeasonOpeningSequence" = c3."SeasonOpeningSequence" AND p5."PlayerCampaignAssignmentId" > p2."PlayerCampaignAssignmentId")))
    ) AS s5 ON p."PlayerId" = s5."PlayerId"
    LEFT JOIN (
        SELECT t."TeamId", t."ClubId", t."GraduationYear", t."LifecycleStatus"
        FROM "Teams" AS t
        WHERE FALSE OR t."ClubId" = 2
    ) AS t0 ON s5."TeamId" = t0."TeamId"
    WHERE (FALSE OR (p."ClubId" = 2 AND (FALSE OR c0."Status" <> 2))) AND p."ClubId" = 2 AND p1."ClubId" = 2 AND c0."ClubId" = 2 AND s0."ClubId" = 2 AND c0."Status" = 0 AND c0."SeasonId" = c1."CurrentSeasonId" AND p."CampaignId" = 2
) AS s6
GROUP BY s6."Key";