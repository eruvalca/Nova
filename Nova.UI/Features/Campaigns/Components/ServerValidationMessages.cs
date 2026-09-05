using Microsoft.AspNetCore.Components.Forms;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Owns contextual server messages and their lifetime across edits and form-context replacement.</summary>
internal sealed class ServerValidationMessages : IDisposable
{
    /// <summary>The current form context receiving server messages and field-change notifications.</summary>
    private EditContext? _editContext;
    /// <summary>The messages contributed by the server independently of DataAnnotations validation.</summary>
    private ValidationMessageStore? _messages;
    /// <summary>The last parent snapshot observed, retained when edits clear its contextual failures.</summary>
    private IReadOnlyDictionary<string, string[]>? _lastErrors;

    /// <summary>Rebinds the message store while preventing an unchanged old snapshot from returning on a new model.</summary>
    /// <param name="editContext">The context that owns the current local form model.</param>
    public void Attach(EditContext editContext)
    {
        if (ReferenceEquals(_editContext, editContext))
        {
            return;
        }

        if (_editContext is not null)
        {
            _editContext.OnFieldChanged -= ClearAfterEdit;
        }

        _editContext = editContext;
        _messages = new ValidationMessageStore(editContext);
        _editContext.OnFieldChanged += ClearAfterEdit;
    }

    /// <summary>Installs only a new server snapshot so parent rerenders cannot undo a user's correction.</summary>
    /// <param name="errors">The parent's current server-validation snapshot.</param>
    /// <param name="mapFieldName">Optional mapping from command field paths to local form field names.</param>
    public void Apply(IReadOnlyDictionary<string, string[]>? errors, Func<string, string>? mapFieldName = null)
    {
        if (_editContext is null || ReferenceEquals(_lastErrors, errors))
        {
            return;
        }

        _lastErrors = errors;
        _messages!.Clear();
        if (errors is not null)
        {
            foreach (var error in errors)
            {
                var fieldName = mapFieldName?.Invoke(error.Key) ?? error.Key;
                _messages.Add(new FieldIdentifier(_editContext.Model, fieldName), error.Value);
            }
        }

        _editContext.NotifyValidationStateChanged();
    }

    /// <summary>Clears contextual failures after any edit so cross-field commands can be revalidated.</summary>
    /// <param name="sender">The context reporting the edit.</param>
    /// <param name="args">The field-change notification.</param>
    private void ClearAfterEdit(object? sender, FieldChangedEventArgs args)
    {
        _messages!.Clear();
        _editContext!.NotifyValidationStateChanged();
    }

    /// <summary>Detaches the current field-change subscription when the form is disposed.</summary>
    public void Dispose()
    {
        if (_editContext is not null)
        {
            _editContext.OnFieldChanged -= ClearAfterEdit;
        }

        _editContext = null;
        _messages = null;
    }
}
