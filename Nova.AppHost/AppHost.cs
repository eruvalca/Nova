var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume();

var novaDatabase = postgres.AddDatabase("novadb", "nova");

builder
    .AddProject<Projects.Nova>("nova")
    .WithReference(novaDatabase)
    .WaitFor(novaDatabase)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
