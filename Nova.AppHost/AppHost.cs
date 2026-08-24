#pragma warning disable ASPIREPROCESSCOMMAND001 // WithProcessCommand is experimental; the repo opts into Aspire preview features via AspireUseCliBundle.

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
var clubCrests = storage.AddBlobContainer("club-crests");

// Process commands run from the Nova project folder, where package.json lives.
var novaDirectory = Path.Combine(builder.AppHostDirectory, "..", "Nova");

var nova = builder
    .AddProject<Projects.Nova>("nova")
    .WithReference(novaDatabase)
    .WaitFor(novaDatabase)
    .WithReference(profilePhotos)
    .WaitFor(profilePhotos)
    .WithReference(clubCrests)
    .WaitFor(clubCrests)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithProcessCommand(
        commandName: "install-npm-deps",
        displayName: "Install npm packages",
        processSpecFactory: _ => new ProcessCommandSpec("npm")
        {
            Arguments = ["ci"],
            WorkingDirectory = novaDirectory,
        },
        commandOptions: new ProcessCommandOptions
        {
            Description = "Runs npm ci in the Nova project on the dev machine. Fixes a stale or missing node_modules tree.",
        })
    .WithProcessCommand(
        commandName: "rebuild-theme",
        displayName: "Rebuild Bootstrap theme",
        processSpecFactory: _ => new ProcessCommandSpec("npm")
        {
            Arguments = ["run", "build:css"],
            WorkingDirectory = novaDirectory,
        },
        commandOptions: new ProcessCommandOptions
        {
            Description = "Runs npm run build:css in the Nova project on the dev machine and recompiles wwwroot/css/bootstrap-theme.css.",
        })
    .WithProcessCommand(
        commandName: "check-contrast",
        displayName: "Run WCAG contrast check",
        processSpecFactory: _ => new ProcessCommandSpec("npm")
        {
            Arguments = ["run", "check:contrast"],
            WorkingDirectory = novaDirectory,
        },
        commandOptions: new ProcessCommandOptions
        {
            Description = "Runs npm run check:contrast in the Nova project on the dev machine (the same check CI runs).",
        });

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

storage.WithCommand(
    name: "clear-club-crests",
    displayName: "Clear club crests",
    executeCommand: context => AppHostCommands.ClearClubCrestsAsync(
        context,
        clubCrests.Resource.Parent.ConnectionStringExpression),
    commandOptions: AppHostCommands.CreateConfirmationOptions(
        "Deletes every blob from the club-crests container."));

builder.Build().Run();
