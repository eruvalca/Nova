using Bunit;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;
using Nova.UI.Features.Campaigns.Components;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Verifies both campaign forms retain correction and field-mapping behavior through the shared message owner.</summary>
public sealed class CampaignFormValidationTests : BunitContext
{
    /// <summary>Verifies correcting metadata permits resubmission despite an unchanged parent error snapshot.</summary>
    [Fact]
    public void CampaignMetadataForm_ResubmitsCorrection_AfterUnchangedParentErrorRerender()
    {
        var submissions = new List<CampaignMetadataFormState>();
        var cut = Render<CampaignMetadataForm>(parameters => parameters
            .Add(component => component.Model, new CampaignMetadataFormState
            {
                CampaignId = 10,
                Name = "Original Draft",
                SeasonId = 5,
                StartDate = new DateOnly(2026, 6, 1)
            })
            .Add(component => component.Seasons,
                [new CampaignSeasonChoice { SeasonId = 5, Name = "Summer", StartDate = new DateOnly(2026, 1, 1) }])
            .Add(component => component.OnValidSubmit,
                EventCallback.Factory.Create<CampaignMetadataFormState>(this, submissions.Add)));
        cut.Find("form").Submit();
        submissions.Count.ShouldBe(1);
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["The campaign name conflicts."],
            ["StartDate"] = ["The dates need review."]
        };
        cut.Render(parameters => parameters.Add(component => component.ServerErrors, errors));
        cut.FindAll(".validation-message").Count.ShouldBe(2);

        cut.Find("#edit-campaign-name").Change("Corrected Draft");
        cut.Render(parameters => parameters.Add(component => component.ServerErrors, errors));
        cut.Find("form").Submit();

        submissions.Count.ShouldBe(2);
        submissions[1].Name.ShouldBe("Corrected Draft");
        cut.FindAll(".validation-message").ShouldBeEmpty();
    }

    /// <summary>Verifies nested season server fields still attach to their visible flattened creation controls.</summary>
    /// <param name="commandField">The nested field path returned by the creation service.</param>
    /// <param name="controlId">The local input that must show the server message.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("InlineSeason.Name", "inline-season-name")]
    [InlineData("InlineSeason.StartDate", "inline-season-start-date")]
    [InlineData("InlineSeason.EndDate", "inline-season-end-date")]
    public void CampaignCreateForm_MapsNestedServerErrors_ToTheirSeasonControls(string commandField, string controlId)
    {
        var cut = Render<CampaignCreateForm>(parameters => parameters
            .Add(component => component.Model, new CampaignCreateFormState
            {
                Name = "Draft",
                UseInlineSeason = true,
                InlineSeasonName = "Summer",
                StartDate = new DateOnly(2026, 6, 1),
                InlineSeasonStartDate = new DateOnly(2026, 1, 1)
            }));

        cut.Render(parameters => parameters.Add(component => component.ServerErrors,
            new Dictionary<string, string[]> { [commandField] = ["Review this season field."] }));

        var control = cut.Find($"#{controlId}");
        control.GetAttribute("aria-invalid").ShouldBe("true");
        var validationMessage = control.ParentElement!.QuerySelector("div.text-danger.small");
        validationMessage.ShouldNotBeNull(cut.Markup);
        validationMessage.TextContent.ShouldBe("Review this season field.");
    }
}
