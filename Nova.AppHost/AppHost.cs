var builder = DistributedApplication.CreateBuilder(args);

builder
    .AddProject<Projects.Nova>("nova")
    .WithExternalHttpEndpoints();

builder.Build().Run();
