using Microsoft.AspNetCore.WebUtilities;
using Nova.SharedKernel.Features.Players;
using Nova.UI.Features.Players.Services;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>Behavioral evidence for directory URLs and nested correction returns.</summary>
public sealed class PlayersUrlStateTests
{
    [Fact]
    public void DirectPlaceReturnSurvivesRecordToEditAndDirectory()
    {
        const string Place = "/campaigns/91?tab=place&placementSearch=Avery&placementPage=3&placementParticipant=88&search=Roster";
        var state = PlayersUrlState.FromReturnDestination(Place);
        state.ReturnUrl.ShouldBe(Place);
        var edited = PlayersUrlState.FromUri(state.ToFormUrl(37));
        PlayersUrlState.FromUri(edited.ToDirectoryUrl()).ReturnUrl.ShouldBe(Place);
        PlayersUrlState.FromReturnDestination("//elsewhere.test").ToFormUrl(37).ShouldBe("/players/37/edit");
        var directory = "/players?search=Lee&page=3&returnUrl=" + Uri.EscapeDataString(Place);
        PlayersUrlState.FromReturnDestination(directory).ToDirectoryUrl().ShouldBe(directory);
    }

    [Fact]
    public void DirectoryFormAndRecordPreserveDiscoveryAndNestedPlaceContext()
    {
        const string Place = "/campaigns/91/place?placeSearch=Avery%20Lee&placePage=3&placeTagIds=8";
        var state = PlayersUrlState.Parse("ARCHIVED", "  12%_\\  ", "2032", "17", "6", "42", Place);
        var directory = state.ToDirectoryUrl();
        PlayersUrlState.FromUri(directory).ShouldBe(state);
        PlayersUrlState.FromUri(state.ToFormUrl()).ShouldBe(state);
        state.ToFormUrl(37).ShouldStartWith("/players/37/edit?");
        PlayersUrlState.FromUri(state.ToFormUrl(37)).ShouldBe(state);
        var recordQuery = QueryHelpers.ParseQuery(new Uri("https://nova.test" + state.ToPlayerUrl(37)).Query);
        recordQuery["returnUrl"].ToString().ShouldBe(directory);
        var draftQuery = QueryHelpers.ParseQuery(new Uri("https://nova.test" + state.ToDraftUrl()).Query);
        draftQuery["returnUrl"].ToString().ShouldBe(Place);
        state.Search.ShouldBe("12%_\\");
    }

    [Theory]
    [InlineData("oops")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("999999999999999999999")]
    public void InvalidNumericValuesNormalizeWithoutThrowing(string invalid)
    {
        var state = PlayersUrlState.Parse("unknown", "  ", invalid, invalid, invalid, invalid, "https://elsewhere.test");
        state.ShouldBe(new PlayersUrlState());
        state.ToDirectoryUrl().ShouldBe("/players");
    }

    [Theory]
    [InlineData("1999")]
    [InlineData("2101")]
    public void UnsupportedYearsAreRemoved(string year)
        => PlayersUrlState.Parse(graduationYear: year).GraduationYear.ShouldBeNull();

    [Fact]
    public void LargestSupportedValuesRemainValidIncludingAnUnavailablePage()
    {
        var state = PlayersUrlState.FromUri("/players?tag=9223372036854775807&page=2147483647&graduationYear=2100");
        state.TagId.ShouldBe(long.MaxValue);
        state.Page.ShouldBe(int.MaxValue);
        state.GraduationYear.ShouldBe(2100);
    }

    [Fact]
    public void OverlongSearchIsRetainedForFeedbackAndNeverTreatedAsAnEmptyFilter()
    {
        var state = PlayersUrlState.Parse(search: new string('x', GetPlayerRosterInput.MaxSearchLength + 1));
        state.IsSearchValid.ShouldBeFalse();
        state.HasFilters.ShouldBeTrue();
        PlayersUrlState.FromUri(state.ToDirectoryUrl()).Search.ShouldBe(state.Search);
        (state with { Search = new string('x', GetPlayerRosterInput.MaxSearchLength) }).IsSearchValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("//elsewhere.test/path")]
    [InlineData("/\\elsewhere.test")]
    [InlineData("/pla\nyers")]
    [InlineData("/bad path")]
    public void UnsafeCorrectionDestinationsFallBackLocally(string destination)
    {
        var state = PlayersUrlState.Parse(returnToDraft: "42", returnUrl: destination);
        state.ReturnUrl.ShouldBeNull();
        state.ToDraftUrl().ShouldBe("/campaigns/42");
        state.ToDirectoryUrl().ShouldBe("/players?returnToDraft=42");
    }

    [Fact]
    public void FingerprintDistinguishesPagesAndFiltersButNotCorrectionContext()
    {
        var state = PlayersUrlState.Parse(search: "Avery", page: "2");
        state.QueryFingerprint.ShouldBe((state with { ReturnToDraft = 42 }).QueryFingerprint);
        string.Equals(state.QueryFingerprint, (state with { Page = 3 }).QueryFingerprint, StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(state.QueryFingerprint, (state with { TagId = 8 }).QueryFingerprint, StringComparison.Ordinal).ShouldBeFalse();
        PlayersUrlState.FromUri("/players?page=2&page=3&tag=2&tag=3").ShouldBe(new PlayersUrlState());
    }
}
