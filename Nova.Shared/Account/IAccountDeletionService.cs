namespace Nova.Shared.Account;

/// <summary>Server-only service that previews and executes deletion of the current user's account.</summary>
public interface IAccountDeletionService
{
    /// <summary>Determines the club-ownership implications of deleting the current user.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A preview describing the deletion scenario.</returns>
    Task<AccountDeletionPreviewDto> GetDeletionPreviewAsync(CancellationToken cancellationToken);

    /// <summary>Deletes the current user's account, including their club when they are its only member.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeleteAccountAsync(CancellationToken cancellationToken);
}
