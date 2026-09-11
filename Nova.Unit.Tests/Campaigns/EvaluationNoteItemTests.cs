using Bunit;
using Nova.SharedKernel.Features.Campaigns;
using Nova.UI.Features.Campaigns.Components;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Shared note rendering, authoritative author controls, and retained conflict drafts.</summary>
public sealed class EvaluationNoteItemTests : BunitContext
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public void NoteHidesMutationControlsWithoutBothAuthorityAndAuthorCapability(bool writable, bool canEdit, bool canDelete)
    {
        var cut = Render<EvaluationNoteItem>(p => p.Add(c => c.Note, Note() with { CanEdit = canEdit, CanDelete = canDelete }).Add(c => c.Writable, writable));
        cut.FindAll("button").ShouldBeEmpty();
        cut.Markup.ShouldContain("Coach Rivera");
        cut.Markup.ShouldContain("Strong positioning.");
        cut.Find("time").GetAttribute("datetime").ShouldBe(Note().CreatedAt.ToString("O"));
    }

    [Fact]
    public void NoteRequiresInlineConfirmationBeforeDeleteCallback()
    {
        var requested = false;
        var confirmed = false;
        var cut = Render<EvaluationNoteItem>(p => p.Add(c => c.Note, Note()).Add(c => c.Writable, true)
            .Add(c => c.OnDelete, () => requested = true).Add(c => c.OnConfirmDelete, () => confirmed = true));
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Delete", StringComparison.Ordinal)).HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Delete", StringComparison.Ordinal)).Click();
        requested.ShouldBeTrue();
        confirmed.ShouldBeFalse();
        cut.Render(p => p.Add(c => c.ConfirmingDelete, true));
        cut.Markup.ShouldContain("Delete this shared note?");
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Delete note", StringComparison.Ordinal)).Click();
        confirmed.ShouldBeTrue();
    }

    [Fact]
    public void NoteConflictShowsCurrentEvidenceBesideRetainedDraftAndRequiresExplicitVersionReview()
    {
        var reviewed = false;
        var cut = Render<EvaluationNoteItem>(p => p.Add(c => c.Note, Note()).Add(c => c.Writable, true)
            .Add(c => c.Editing, true).Add(c => c.Draft, "My unsaved revision.")
            .Add(c => c.ExpectedVersion, Guid.NewGuid()).Add(c => c.OnReviewVersion, () => reviewed = true));
        cut.Find("textarea").GetAttribute("value").ShouldBe("My unsaved revision.");
        cut.Find("[role=alert]").TextContent.ShouldContain("This note has changed");
        cut.Find(".evaluation-note-current").TextContent.ShouldBe("Strong positioning.");
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Save changes", StringComparison.Ordinal)).HasAttribute("disabled").ShouldBeTrue();
        reviewed.ShouldBeFalse();
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Use current version for this edit", StringComparison.Ordinal)).HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Use current version for this edit", StringComparison.Ordinal)).Click();
        reviewed.ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NoteEditorRemainsCopyableButCannotSaveWhileReadOnlyOrPending(bool writable, bool pending)
    {
        var note = Note();
        var cut = Render<EvaluationNoteItem>(p => p.Add(c => c.Note, note).Add(c => c.Writable, writable)
            .Add(c => c.Pending, pending).Add(c => c.Editing, true).Add(c => c.Draft, "Keep this draft")
            .Add(c => c.ExpectedVersion, note.Version));
        cut.Find("textarea").HasAttribute("readonly").ShouldBeTrue();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Keep this draft");
        var saves = cut.FindAll("button").Where(b => string.Equals(b.TextContent, "Save changes", StringComparison.Ordinal)).ToList();
        if (writable) { saves.Single().HasAttribute("disabled").ShouldBeTrue(); }
        else { saves.ShouldBeEmpty(); }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(220, false)]
    [InlineData(221, true)]
    [InlineData(4000, true)]
    public void LongNoteKeepsCompleteEvidenceBehindNativeDisclosure(int length, bool disclosure)
    {
        var content = new string('x', length);
        var cut = Render<EvaluationNoteItem>(p => p.Add(c => c.Note, Note() with { Content = content }));
        cut.FindAll("details").Count.ShouldBe(disclosure ? 1 : 0);
        cut.Markup.ShouldContain(content);
    }

    private static CampaignParticipantNoteDto Note() => new(1, "Strong positioning.", "Coach Rivera",
        new DateTimeOffset(2026, 9, 1, 10, 30, 0, TimeSpan.Zero), null, true, true, Guid.Parse("00000001-0000-0000-0000-000000000001"));
}
