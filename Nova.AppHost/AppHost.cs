var builder = DistributedApplication.CreateBuilder(args);

builder
    .AddProject<Projects.Nova>("nova")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
