using Nova.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume();

var novaDatabase = postgres.AddDatabase("novadb", "nova");

var storage = builder
    .AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume());

var profilePhotos = storage.AddBlobContainer("profile-photos");

var nova = builder
    .AddProject<Projects.Nova>("nova")
    .WithReference(novaDatabase)
    .WaitFor(novaDatabase)
    .WithReference(profilePhotos)
    .WaitFor(profilePhotos)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

postgres.WithCommand(
    name: "reset-db",
    displayName: "Reset nova database",
    executeCommand: context => AppHostCommands.ResetDatabaseAsync(
        context,
        novaDatabase.Resource.ConnectionStringExpression,
        novaDatabase.Resource.DatabaseName,
        nova.Resource),
    commandOptions: AppHostCommands.CreateConfirmationOptions(
        "Drops and recreates the nova database, then restarts Nova so migrations run again."));

storage.WithCommand(
    name: "clear-profile-photos",
    displayName: "Clear profile photos",
    executeCommand: context => AppHostCommands.ClearProfilePhotosAsync(
        context,
        profilePhotos.Resource.Parent.ConnectionStringExpression),
    commandOptions: AppHostCommands.CreateConfirmationOptions(
        "Deletes every blob from the profile-photos container."));

builder.Build().Run();
