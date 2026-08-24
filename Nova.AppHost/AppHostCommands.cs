using Aspire.Hosting.ApplicationModel;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Nova.AppHost;

/// <summary>
/// Implements destructive development commands exposed by the AppHost.
/// </summary>
internal static class AppHostCommands
{
    /// <summary>The name of the command argument that carries the destructive-operation confirmation.</summary>
    private const string ConfirmationArgumentName = "confirm";

    /// <summary>The only accepted confirmation value for destructive operations.</summary>
    private const string RequiredConfirmation = "yes";

    /// <summary>
    /// Creates the shared required confirmation argument for destructive commands.
    /// </summary>
    /// <param name="description">The command description shown by Aspire clients.</param>
    /// <returns>Command options containing a required single-choice confirmation.</returns>
    internal static CommandOptions CreateConfirmationOptions(string description) =>
        new()
        {
            Description = description,
            Arguments =
            [
                new InteractionInput
                {
                    Name = ConfirmationArgumentName,
                    Label = "Confirm",
                    Description = "Select yes to confirm this destructive operation.",
                    InputType = InputType.Choice,
                    Options = [KeyValuePair.Create(RequiredConfirmation, "Yes")],
                    Required = true,
                },
            ],
        };

    /// <summary>
    /// Drops and recreates the Nova database, then restarts the Nova project resource.
    /// </summary>
    /// <param name="context">The Aspire command execution context.</param>
    /// <param name="connectionStringExpression">The Nova database connection string expression.</param>
    /// <param name="databaseName">The name of the database to drop and recreate.</param>
    /// <param name="novaResource">The Nova project resource to restart after the reset.</param>
    /// <returns>The outcome of the database reset and project restart.</returns>
    internal static async Task<ExecuteCommandResult> ResetDatabaseAsync(
        ExecuteCommandContext context,
        ReferenceExpression connectionStringExpression,
        string databaseName,
        IResource novaResource)
    {
        if (!IsConfirmed(context))
        {
            return CommandResults.Failure("The database was not reset because confirmation was not 'yes'.");
        }

        var connectionString = await connectionStringExpression.GetValueAsync(context.CancellationToken);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return CommandResults.Failure("The Nova database connection string could not be resolved.");
        }

        var maintenanceConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync(context.CancellationToken);

        var quotedDatabaseName = QuoteIdentifier(databaseName);
        await ExecuteDatabaseCommandAsync(
            connection,
            $"DROP DATABASE {quotedDatabaseName} WITH (FORCE);",
            context.CancellationToken);
        await ExecuteDatabaseCommandAsync(
            connection,
            $"CREATE DATABASE {quotedDatabaseName};",
            context.CancellationToken);

        var commandService = context.Services.GetRequiredService<ResourceCommandService>();
        var restartResult = await commandService.ExecuteCommandAsync(
            novaResource,
            KnownResourceCommands.RestartCommand,
            context.CancellationToken);

        if (!restartResult.Success)
        {
            var restartError = restartResult.Message ?? "Unknown restart failure.";
            return CommandResults.Failure(
                $"The database was reset, but Nova could not be restarted: {restartError} Restart Nova manually.");
        }

        return CommandResults.Success(
            "The Nova database was reset and Nova was restarted so migrations can run again.");
    }

    /// <summary>
    /// Deletes all blobs from the profile photo container.
    /// </summary>
    /// <param name="context">The Aspire command execution context.</param>
    /// <param name="connectionStringExpression">The profile photo storage connection string expression.</param>
    /// <returns>The outcome and number of deleted blobs.</returns>
    internal static async Task<ExecuteCommandResult> ClearProfilePhotosAsync(
        ExecuteCommandContext context,
        ReferenceExpression connectionStringExpression)
    {
        if (!IsConfirmed(context))
        {
            return CommandResults.Failure("Profile photos were not cleared because confirmation was not 'yes'.");
        }

        var connectionString = await connectionStringExpression.GetValueAsync(context.CancellationToken);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return CommandResults.Failure("The profile photo storage connection string could not be resolved.");
        }

        var container = new BlobServiceClient(connectionString)
            .GetBlobContainerClient("profile-photos");

        if (!await container.ExistsAsync(context.CancellationToken))
        {
            return CommandResults.Success("The profile-photos container does not exist; 0 blobs were deleted.");
        }

        var deletedCount = 0;
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: context.CancellationToken))
        {
            var deleted = await container.DeleteBlobIfExistsAsync(
                blob.Name,
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: context.CancellationToken);
            if (deleted.Value)
            {
                deletedCount++;
            }
        }

        return CommandResults.Success($"Cleared {deletedCount} blob(s) from the profile-photos container.");
    }

    /// <summary>
    /// Deletes all blobs from the club crest container.
    /// </summary>
    /// <param name="context">The Aspire command execution context.</param>
    /// <param name="connectionStringExpression">The club crest storage connection string expression.</param>
    /// <returns>The outcome and number of deleted blobs.</returns>
    internal static async Task<ExecuteCommandResult> ClearClubCrestsAsync(
        ExecuteCommandContext context,
        ReferenceExpression connectionStringExpression)
    {
        if (!IsConfirmed(context))
        {
            return CommandResults.Failure("Club crests were not cleared because confirmation was not 'yes'.");
        }

        var connectionString = await connectionStringExpression.GetValueAsync(context.CancellationToken);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return CommandResults.Failure("The club crest storage connection string could not be resolved.");
        }

        var container = new BlobServiceClient(connectionString)
            .GetBlobContainerClient("club-crests");

        if (!await container.ExistsAsync(context.CancellationToken))
        {
            return CommandResults.Success("The club-crests container does not exist; 0 blobs were deleted.");
        }

        var deletedCount = 0;
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: context.CancellationToken))
        {
            var deleted = await container.DeleteBlobIfExistsAsync(
                blob.Name,
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: context.CancellationToken);
            if (deleted.Value)
            {
                deletedCount++;
            }
        }

        return CommandResults.Success($"Cleared {deletedCount} blob(s) from the club-crests container.");
    }

    /// <summary>
    /// Determines whether the destructive command received the required confirmation value.
    /// </summary>
    /// <param name="context">The Aspire command execution context.</param>
    /// <returns><see langword="true"/> when the confirmation value is exactly <c>yes</c>.</returns>
    private static bool IsConfirmed(ExecuteCommandContext context) =>
        string.Equals(
            context.Arguments.GetString(ConfirmationArgumentName),
            RequiredConfirmation,
            StringComparison.Ordinal);

    /// <summary>
    /// Quotes a database identifier for safe interpolation into administrative SQL statements.
    /// </summary>
    /// <param name="identifier">The identifier to quote.</param>
    /// <returns>The identifier wrapped in double quotes with embedded quotes escaped.</returns>
    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>
    /// Executes a single database administration statement.
    /// </summary>
    /// <param name="connection">The open PostgreSQL maintenance connection.</param>
    /// <param name="commandText">The SQL statement to execute.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <returns>A task that completes when the statement has executed.</returns>
    private static async Task ExecuteDatabaseCommandAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
