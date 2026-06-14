using Bunit;
using Nova.UI.Shared;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for ConfirmDeleteDialog component: confirmation checkbox state management,
/// button enable/disable logic, and parameter rendering.
/// </summary>
public class ConfirmDeleteDialogTests
{
    [Fact]
    public void ConfirmButton_IsDisabled_Initially()
    {
        // Arrange
        using var testContext = new BunitContext();

        // Act
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "My Club")
                .Add(p => p.FormId, "delete-form")
        );

        // Assert
        var submitButton = cut.Find("button[type='submit']");
        // In Blazor, disabled="@(false)" omits the disabled attribute entirely
        submitButton.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void ConfirmButton_IsEnabled_AfterConfirmationCheckboxIsChecked()
    {
        // Arrange
        using var testContext = new BunitContext();
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "My Club")
                .Add(p => p.FormId, "delete-form")
        );

        // Act
        var checkbox = cut.Find("input[type='checkbox']");
        checkbox.Change(true);

        // Assert
        var submitButton = cut.Find("button[type='submit']");
        submitButton.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void ConfirmButton_IsDisabled_AfterConfirmationCheckboxIsUnchecked()
    {
        // Arrange
        using var testContext = new BunitContext();
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "My Club")
                .Add(p => p.FormId, "delete-form")
        );
        var checkbox = cut.Find("input[type='checkbox']");

        // Act
        checkbox.Change(true);
        checkbox.Change(false);

        // Assert
        var submitButton = cut.Find("button[type='submit']");
        submitButton.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void DisplaysClubName_InWarningText()
    {
        // Arrange
        using var testContext = new BunitContext();
        var clubName = "Test Soccer Club";

        // Act
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, clubName)
                .Add(p => p.FormId, "delete-form")
        );

        // Assert - Club name should be displayed in the modal
        var content = cut.Markup;
        content.ShouldContain(clubName);
    }

    [Fact]
    public void FormId_IsSetOnSubmitButton()
    {
        // Arrange
        using var testContext = new BunitContext();
        var formId = "my-delete-form";

        // Act
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "My Club")
                .Add(p => p.FormId, formId)
        );

        // Assert
        var submitButton = cut.Find("button[type='submit']");
        submitButton.GetAttribute("form").ShouldBe(formId);
    }

    [Fact]
    public void ConfirmationCheckbox_IsNotCheckedInitially()
    {
        // Arrange
        using var testContext = new BunitContext();

        // Act
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "My Club")
                .Add(p => p.FormId, "delete-form")
        );

        // Assert
        var checkbox = cut.Find("input[type='checkbox']");
        checkbox.HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public void WarningText_ContainsAccountDeletionMessage()
    {
        // Arrange
        using var testContext = new BunitContext();

        // Act
        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "My Club")
                .Add(p => p.FormId, "delete-form")
        );

        // Assert
        cut.Markup.ShouldContain("Permanently delete your club and account");
        cut.Markup.ShouldContain("I understand this will permanently delete my club and all of its data");
    }

    [Fact]
    public async Task ShowAsync_InvokesNovaShowModal_WithConfirmDeleteModalSelector()
    {
        // Arrange
        using var testContext = new BunitContext();
        testContext.JSInterop.SetupVoid("novaShowModal", "#confirm-delete-modal").SetVoidResult();

        var cut = testContext.Render<ConfirmDeleteDialog>(
            parameters => parameters
                .Add(p => p.ClubName, "Test Club")
                .Add(p => p.FormId, "test-form")
        );

        // Act
        await cut.Instance.ShowAsync();

        // Assert
        testContext.JSInterop.VerifyInvoke("novaShowModal").Arguments.ShouldContain("#confirm-delete-modal");
    }
}
