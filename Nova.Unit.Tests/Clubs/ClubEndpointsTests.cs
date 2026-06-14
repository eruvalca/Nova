using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="ClubEndpoints"/> URL builders and <see cref="ClubDto"/>, 
/// <see cref="ClubJoinRequestDto"/>, and <see cref="CreateClubInput"/> record equality.
/// </summary>
public class ClubEndpointsTests
{
    [Fact]
    public void CreateJoinRequestUrl_BuildsCorrectUrl_WithClubId()
    {
        // Arrange
        const long clubId = 42;

        // Act
        var url = ClubEndpoints.CreateJoinRequestUrl(clubId);

        // Assert
        url.ShouldBe("/api/clubs/42/join-requests");
    }

    [Fact]
    public void CreateJoinRequestUrl_BuildsCorrectUrl_WithLargeClubId()
    {
        // Arrange
        const long clubId = 9876543210;

        // Act
        var url = ClubEndpoints.CreateJoinRequestUrl(clubId);

        // Assert
        url.ShouldBe("/api/clubs/9876543210/join-requests");
    }

    [Fact]
    public void CancelJoinRequestUrl_BuildsCorrectUrl_WithRequestId()
    {
        // Arrange
        const long requestId = 123;

        // Act
        var url = ClubEndpoints.CancelJoinRequestUrl(requestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/123");
    }

    [Fact]
    public void CancelJoinRequestUrl_BuildsCorrectUrl_WithLargeRequestId()
    {
        // Arrange
        const long requestId = 1234567890;

        // Act
        var url = ClubEndpoints.CancelJoinRequestUrl(requestId);

        // Assert
        url.ShouldBe("/api/clubs/join-requests/1234567890");
    }

    [Fact]
    public void SearchUrl_ReturnsBaseUrl_WhenQueryIsNull()
    {
        // Arrange & Act
        var url = ClubEndpoints.SearchUrl(null);

        // Assert
        url.ShouldBe("/api/clubs/search");
    }

    [Fact]
    public void SearchUrl_ReturnsBaseUrl_WhenQueryIsEmpty()
    {
        // Arrange & Act
        var url = ClubEndpoints.SearchUrl(string.Empty);

        // Assert
        url.ShouldBe("/api/clubs/search");
    }

    [Fact]
    public void SearchUrl_ReturnsBaseUrl_WhenQueryIsWhitespace()
    {
        // Arrange & Act
        var url = ClubEndpoints.SearchUrl("   ");

        // Assert
        url.ShouldBe("/api/clubs/search");
    }

    [Fact]
    public void SearchUrl_IncludesQuery_WhenQueryIsProvided()
    {
        // Arrange
        const string query = "Manchester United";

        // Act
        var url = ClubEndpoints.SearchUrl(query);

        // Assert
        url.ShouldBe("/api/clubs/search?q=Manchester%20United");
    }

    [Fact]
    public void SearchUrl_UrlEncodesSpaces_InQuery()
    {
        // Arrange
        const string query = "New York";

        // Act
        var url = ClubEndpoints.SearchUrl(query);

        // Assert
        url.ShouldBe("/api/clubs/search?q=New%20York");
    }

    [Fact]
    public void SearchUrl_UrlEncodesSpecialCharacters_InQuery()
    {
        // Arrange
        const string query = "Royal Tenenbaums & Friends";

        // Act
        var url = ClubEndpoints.SearchUrl(query);

        // Assert
        url.ShouldContain("%26");  // '&' encoded
    }

    [Fact]
    public void SearchUrl_HandlesQueryWithPlusSign()
    {
        // Arrange
        const string query = "FC Plus";

        // Act
        var url = ClubEndpoints.SearchUrl(query);

        // Assert
        url.ShouldBe("/api/clubs/search?q=FC%20Plus");
    }

    [Fact]
    public void ClubDto_EqualsOtherInstance_WithSameValues()
    {
        // Arrange
        var club1 = new ClubDto(ClubId: 1, Name: "FC United", City: "Manchester", State: "England");
        var club2 = new ClubDto(ClubId: 1, Name: "FC United", City: "Manchester", State: "England");

        // Act & Assert
        club1.ShouldBe(club2);
        (club1 == club2).ShouldBeTrue();
    }

    [Fact]
    public void ClubDto_NotEqualsOtherInstance_WithDifferentClubId()
    {
        // Arrange
        var club1 = new ClubDto(ClubId: 1, Name: "FC United", City: "Manchester", State: "England");
        var club2 = new ClubDto(ClubId: 2, Name: "FC United", City: "Manchester", State: "England");

        // Act & Assert
        club1.ShouldNotBe(club2);
        (club1 != club2).ShouldBeTrue();
    }

    [Fact]
    public void ClubDto_Deconstructs_Correctly()
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
    public void ClubJoinRequestDto_EqualsOtherInstance_WithSameValues()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var request1 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);
        var request2 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);

        // Act & Assert
        request1.ShouldBe(request2);
        (request1 == request2).ShouldBeTrue();
    }

    [Fact]
    public void ClubJoinRequestDto_NotEqualsOtherInstance_WithDifferentRequestId()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var request1 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);
        var request2 = new ClubJoinRequestDto(
            ClubJoinRequestId: 11,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);

        // Act & Assert
        request1.ShouldNotBe(request2);
        (request1 != request2).ShouldBeTrue();
    }

    [Fact]
    public void ClubJoinRequestDto_NotEqualsOtherInstance_WithDifferentStatus()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var request1 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            Status: RequestStatus.Pending,
            CreatedAt: createdAt);
        var request2 = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Manchester City",
            RequestingUserId: 99,
            Status: RequestStatus.Approved,
            CreatedAt: createdAt);

        // Act & Assert
        request1.ShouldNotBe(request2);
        (request1 != request2).ShouldBeTrue();
    }

    [Fact]
    public void ClubJoinRequestDto_Deconstructs_Correctly()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero);
        var request = new ClubJoinRequestDto(
            ClubJoinRequestId: 42,
            ClubId: 7,
            ClubName: "Arsenal FC",
            RequestingUserId: 88,
            Status: RequestStatus.Rejected,
            CreatedAt: createdAt);

        // Act
        var (requestId, clubId, clubName, userId, status, created) = request;

        // Assert
        requestId.ShouldBe(42);
        clubId.ShouldBe(7);
        clubName.ShouldBe("Arsenal FC");
        userId.ShouldBe(88);
        status.ShouldBe(RequestStatus.Rejected);
        created.ShouldBe(createdAt);
    }

    [Fact]
    public void CreateClubInput_EqualsOtherInstance_WithSameValues()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London" };
        var input2 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London" };

        // Act & Assert
        input1.ShouldBe(input2);
        (input1 == input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInput_NotEqualsOtherInstance_WithDifferentName()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London" };
        var input2 = new CreateClubInput { Name = "Fulham FC", City = "London", State = "Greater London" };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInput_NotEqualsOtherInstance_WithDifferentCity()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London" };
        var input2 = new CreateClubInput { Name = "Chelsea FC", City = "Birmingham", State = "Greater London" };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInput_NotEqualsOtherInstance_WithDifferentState()
    {
        // Arrange
        var input1 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "Greater London" };
        var input2 = new CreateClubInput { Name = "Chelsea FC", City = "London", State = "England" };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void CreateClubInput_HasCorrectProperties()
    {
        // Arrange
        var input = new CreateClubInput { Name = "Tottenham Hotspur", City = "London", State = "Greater London" };

        // Act & Assert
        input.Name.ShouldBe("Tottenham Hotspur");
        input.City.ShouldBe("London");
        input.State.ShouldBe("Greater London");
    }
}
