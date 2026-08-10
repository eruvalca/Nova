using Cropper.Blazor.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nova.Client.Services;
using Nova.Client.Telemetry;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Features.Players;
using Nova.Shared.Features.Teams;

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
builder.Services.AddScoped<IPlayerService, HttpPlayerService>();
builder.Services.AddScoped<IPlayerLifecycleService, HttpPlayerLifecycleService>();
builder.Services.AddScoped<IPlayerManagementService, HttpPlayerManagementService>();
builder.Services.AddScoped<ITeamManagementService, HttpTeamManagementService>();
builder.Services.AddScoped<ITeamLifecycleService, HttpTeamLifecycleService>();
builder.Services.AddScoped<ITeamRosterService, HttpTeamRosterService>();
builder.Services.AddScoped<ITeamDetailService, HttpTeamDetailService>();
builder.Services.AddScoped<IPlayerDetailService, HttpPlayerDetailService>();
builder.Services.AddScoped<ICampaignCreationService, HttpCampaignCreationService>();
builder.Services.AddScoped<ICampaignQueryService, HttpCampaignQueryService>();
builder.Services.AddScoped<ICampaignParticipantQueryService, HttpCampaignParticipantQueryService>();
builder.Services.AddScoped<ICampaignMetadataService, HttpCampaignMetadataService>();
builder.Services.AddScoped<ISeasonMetadataService, HttpSeasonMetadataService>();

await builder.Build().RunAsync();
