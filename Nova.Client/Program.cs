using Cropper.Blazor.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nova.Client.Services;
using Nova.Client.Telemetry;
using Nova.Shared.Clubs;
using Nova.Shared.Photos;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();

    return new HttpClient(new TraceParentPropagatingHandler { InnerHandler = new HttpClientHandler() })
    {
        BaseAddress = new Uri(navigationManager.BaseUri)
    };
});

builder.Services.AddCropper();
builder.Services.AddScoped<IProfilePhotoService, HttpProfilePhotoService>();
builder.Services.AddScoped<IClubService, HttpClubService>();
builder.Services.AddScoped<IClubJoinRequestService, HttpClubJoinRequestService>();

await builder.Build().RunAsync();
