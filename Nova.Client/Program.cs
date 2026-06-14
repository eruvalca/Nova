using Cropper.Blazor.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nova.Client.Services;
using Nova.Client.Telemetry;
using Nova.Shared.Account;
using Nova.Shared.Clubs;
using Nova.Shared.Photos;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddTransient<TraceParentPropagatingHandler>();
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<TraceParentPropagatingHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler)
    {
        BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
    };
});

builder.Services.AddCropper();
builder.Services.AddScoped<IProfilePhotoService, HttpProfilePhotoService>();
builder.Services.AddScoped<IClubService, HttpClubService>();
builder.Services.AddScoped<IClubJoinRequestService, HttpClubJoinRequestService>();
builder.Services.AddScoped<IClubMemberService, HttpClubMemberService>();

await builder.Build().RunAsync();
