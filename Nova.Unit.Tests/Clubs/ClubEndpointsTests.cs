using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Clubs;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="ClubEndpoints"/> URL builders and <see cref="ClubDto"/>,
/// <see cref="ClubJoinRequestDto"/>, and <see cref="CreateClubInput"/> record equality.
/// </summary>
public class ClubEndpointsTests
{
    [Fact]
    public void CreateJoinRequestUrlBuildsCorrectUrlWithClubId()
    {
        // Arrange
        const long ClubId = 42;

        // Act
        var url = ClubEndpoints.CreateJoinRequestUrl(ClubId);

        // Assert
        url.ShouldBe("/api/clubs/42/join-requests");
    }

    [Fact]
    public void CreateJoinRequestUrlBuildsCorrectUrlWithLargeClubId()
    {
        // Arrange
        const long ClubId = 9876543210;

        // Act
        var url = ClubEndpoints.CreateJoinRequestUrl(ClubId);

        // Assert
        url.ShouldBe("/api/clubs/9876543210/join-requests");
    }

    [Fact]
    public void CancelJoinRequestUrlBuildsCorrectUrlWithRequestId()
    {
        // Arrange
        const long RequestId = 123;

        // Act
        var url = ClubEndpoints.CancelJoinRequestUrl(RequestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/123");
    }

    [Fact]
    public void CancelJoinRequestUrlBuildsCorrectUrlWithLargeRequestId()
    {
        // Arrange
        const long RequestId = 1234567890;

        // Act
        var url = ClubEndpoints.CancelJoinRequestUrl(RequestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/1234567890");
    }

    [Fact]
    public void AdminJoinRequestsUrlBuildsCorrectUrlWithClubId()
    {
        // Arrange
        const long ClubId = 42;

        // Act
        var url = ClubEndpoints.AdminJoinRequestsUrl(ClubId);

        // Assert
        url.ShouldBe("/api/clubs/42/admin/join-requests");
    }

    [Fact]
    public void AdminJoinRequestsUrlBuildsCorrectUrlWithLargeClubId()
    {
        // Arrange
        const long ClubId = 9876543210;

        // Act
        var url = ClubEndpoints.AdminJoinRequestsUrl(ClubId);

        // Assert
        url.ShouldBe("/api/clubs/9876543210/admin/join-requests");
    }

    [Fact]
    public void ApproveJoinRequestUrlBuildsCorrectUrlWithRequestId()
    {
        // Arrange
        const long RequestId = 7;

        // Act
        var url = ClubEndpoints.ApproveJoinRequestUrl(RequestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/7/approve");
    }

    [Fact]
    public void ApproveJoinRequestUrlBuildsCorrectUrlWithLargeRequestId()
    {
        // Arrange
        const long RequestId = 1234567890;

        // Act
        var url = ClubEndpoints.ApproveJoinRequestUrl(RequestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/1234567890/approve");
    }

    [Fact]
    public void RejectJoinRequestUrlBuildsCorrectUrlWithRequestId()
    {
        // Arrange
        const long RequestId = 7;

        // Act
        var url = ClubEndpoints.RejectJoinRequestUrl(RequestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/7/reject");
    }

    [Fact]
    public void RejectJoinRequestUrlBuildsCorrectUrlWithLargeRequestId()
    {
        // Arrange
        const long RequestId = 1234567890;

        // Act
        var url = ClubEndpoints.RejectJoinRequestUrl(RequestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/1234567890/reject");
    }

    [Fact]
    public void AdminJoinRequestsRelativeHasCorrectValue() =>
        // Act & Assert
        ClubEndpoints.AdminJoinRequestsRelative.ShouldBe("{clubId:long}/admin/join-requests");

    [Fact]
    public void ApproveJoinRequestRelativeHasCorrectValue() =>
        // Act & Assert
        ClubEndpoints.ApproveJoinRequestRelative.ShouldBe("join-requests/{requestId:long}/approve");

    [Fact]
    public void RejectJoinRequestRelativeHasCorrectValue() =>
        // Act & Assert
        ClubEndpoints.RejectJoinRequestRelative.ShouldBe("join-requests/{requestId:long}/reject");

    [Fact]
    public void GroupPrefixHasNotChanged() =>
        // Act & Assert
        ClubEndpoints.GroupPrefix.ShouldBe("/api/clubs");

    [Fact]
    public void CompleteHasNotChanged() =>
        // Act & Assert
        ClubEndpoints.Complete.ShouldBe("/Clubs/Onboarding/Complete");

    [Fact]
    public void PendingRequestHasNotChanged() =>
        // Act & Assert
        ClubEndpoints.PendingRequest.ShouldBe("/api/clubs/join-requests/pending");

    [Fact]
    public void SearchUrlReturnsBaseUrlWhenQueryIsNull()
    {
        // Arrange & Act
        var url = ClubEndpoints.SearchUrl(null);

        // Assert
        url.ShouldBe("/api/clubs/search");
    }

    [Fact]
    public void SearchUrlReturnsBaseUrlWhenQueryIsEmpty()
    {
        // Arrange & Act
        var url = ClubEndpoints.SearchUrl(string.Empty);

        // Assert
        url.ShouldBe("/api/clubs/search");
    }

    [Fact]
    public void SearchUrlReturnsBaseUrlWhenQueryIsWhitespace()
    {
        // Arrange & Act
        var url = ClubEndpoints.SearchUrl("   ");

        // Assert
        url.ShouldBe("/api/clubs/search");
    }

    [Fact]
    public void SearchUrlIncludesQueryWhenQueryIsProvided()
    {
        // Arrange
        const string Query = "Manchester United";

        // Act
        var url = ClubEndpoints.SearchUrl(Query);

        // Assert
        url.ShouldBe("/api/clubs/search?q=Manchester%20United");
    }

    [Fact]
    public void SearchUrlUrlEncodesSpacesInQuery()
    {
        // Arrange
        const string Query = "New York";

        // Act
        var url = ClubEndpoints.SearchUrl(Query);

        // Assert
        url.ShouldBe("/api/clubs/search?q=New%20York");
    }

    [Fact]
    public void SearchUrlUrlEncodesSpecialCharactersInQuery()
    {
        // Arrange
        const string Query = "Royal Tenenbaums & Friends";

        // Act
        var url = ClubEndpoints.SearchUrl(Query);

        // Assert
        url.ShouldContain("%26");  // '&' encoded
    }

    [Fact]
    public void SearchUrlHandlesQueryWithPlusSign()
    {
        // Arrange
        const string Query = "FC Plus";

        // Act
        var url = ClubEndpoints.SearchUrl(Query);

        // Assert
        url.ShouldBe("/api/clubs/search?q=FC%20Plus");
    }

    [Fact]
    public void ClubDtoEqualsOtherInstanceWithSameValues()
    {
        // Arrange
        var club1 = new ClubDto(ClubId: 1, Name: "FC United", City: "Manchester", State: "England");
        var club2 = new ClubDto(ClubId: 1, Name: "FC United", City: "Manchester", State: "England");

        // Act & Assert
        club1.ShouldBe(club2);
        (club1 == club2).ShouldBeTrue();
    }

    [Fact]
    public void ClubDtoNotEqualsOtherInstanceWithDifferentClubId()
    {
        // Arrange
        var club1 = new ClubDto(ClubId: 1, Name: "FC United", City: "Manchester", State: "England");
        var club2 = new ClubDto(ClubId: 2, Name: "FC United", City: "Manchester", State: "England");

        // Act & Assert
        club1.ShouldNotBe(club2);
        (club1 != club2).ShouldBeTrue();
    }

    [Fact]
    public void ClubDtoDeconstructsCorrectly()
    {
        // Arrange
        var club = new ClubDto(ClubId: 42, Name: "Liverpool FC", City: "Liverpool", State: "Merseyside");

        // Act
        var (clubId, name, city, state) = club;

        // Assert
        clubId.ShouldBe(42);
        name.ShouldBe("Liverpool FC");
        city.ShouldBe("Liverpool");
        state.ShouldBe("Merseyside");
    }

    [Fact]
    public void ClubJoinRequestDtoEqualsOtherInstanceWithSameValues()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var request1 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);
        var request2 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);

        // Act & Assert
        request1.ShouldBe(request2);
        (request1 == request2).ShouldBeTrue();
    }

    [Fact]
    public void ClubJoinRequestDtoNotEqualsOtherInstanceWithDifferentRequestId()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var request1 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);
        var request2 = new ClubJoinRequestDto(
            ClubJoinRequestId: 11,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);

        // Act & Assert
        request1.ShouldNotBe(request2);
        (request1 != request2).ShouldBeTrue();
    }

    [Fact]
    public void ClubJoinRequestDtoNotEqualsOtherInstanceWithDifferentStatus()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var request1 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);
        var request2 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Approved,
            CreatedAt: createdAt);

        // Act & Assert
        request1.ShouldNotBe(request2);
        (request1 != request2).ShouldBeTrue();
    }

    [Fact]
    public void ClubJoinRequestDtoDeconstructsCorrectly()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero);
        var request = new ClubJoinRequestDto(
            ClubJoinRequestId: 42,
            ClubId: 7,
            ClubName: "Arsenal FC",
            RequestingUserId: 88,
            RequestingUserName: "Test User",
            Status: RequestStatus.Rejected,
            CreatedAt: createdAt);

        // Act
        var (requestId, clubId, clubName, userId, userName, status, created) = request;

        // Assert
        requestId.ShouldBe(42);
        clubId.ShouldBe(7);
        clubName.ShouldBe("Arsenal FC");
        userId.ShouldBe(88);
        userName.ShouldBe("Test User");
        status.ShouldBe(RequestStatus.Rejected);
        created.ShouldBe(createdAt);
    }

    [Fact]
    public void CreateClubInputEqualsOtherInstanceWithSameValues()
    {
        // Arrange
        var crestBytes = TestImages.CreateJpeg();
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London", CrestContent = crestBytes, CrestContentType = "image/jpeg" };
        var input2 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London", CrestContent = crestBytes, CrestContentType = "image/jpeg" };

        // Act & Assert
        input1.ShouldBe(input2);
        (input1 == input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInputNotEqualsOtherInstanceWithDifferentName()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" };
        var input2 = new CreateClubInput { Name = "Fulham FC", City = "London", State = "Greater London", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInputNotEqualsOtherInstanceWithDifferentCity()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" };
        var input2 = new CreateClubInput { Name = "Chelsea FC", City = "Birmingham", State = "Greater London", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInputNotEqualsOtherInstanceWithDifferentState()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" };
        var input2 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "England", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInputHasCorrectProperties()
    {
        // Arrange
        var crestBytes = TestImages.CreateJpeg();
        var input = new CreateClubInput
        {
            Name = "Tottenham Hotspur",
            City = "London",
            State = "Greater London",
            CrestContent = crestBytes,
            CrestContentType = "image/jpeg"
        };

        // Act & Assert
        input.Name.ShouldBe("Tottenham Hotspur");
        input.City.ShouldBe("London");
        input.State.ShouldBe("Greater London");
        input.CrestContent.ShouldBeSameAs(crestBytes);
        input.CrestContentType.ShouldBe("image/jpeg");
    }
}
