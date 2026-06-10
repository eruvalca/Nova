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

builder
    .AddProject<Projects.Nova>("nova")
    .WithReference(novaDatabase)
    .WaitFor(novaDatabase)
    .WithReference(profilePhotos)
    .WaitFor(profilePhotos)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
