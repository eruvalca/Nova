using Cropper.Blazor.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nova.Client.Services;
using Nova.Client.Services.Activity;
using Nova.Client.Services.Attention;
using Nova.Client.Services.Campaigns;
using Nova.Client.Services.Clubs;
using Nova.Client.Services.Dashboard;
using Nova.Client.Services.Photos;
using Nova.Client.Services.Players;
using Nova.Client.Services.Seasons;
using Nova.Client.Services.Tags;
using Nova.Client.Services.Teams;
using Nova.Client.Telemetry;
using Nova.SharedKernel.Features.Account;
using Nova.SharedKernel.Features.Activity;
using Nova.SharedKernel.Features.Attention;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Dashboard;
using Nova.SharedKernel.Features.Photos;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.UI.Common;
using Nova.UI.Features.Players;

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
builder.Services.AddScoped<IClubIdentityQueryService, HttpClubIdentityQueryService>();
builder.Services.AddScoped<IClubCrestService, HttpClubCrestService>();
builder.Services.AddScoped<ICropperCanvasExporter, CropperCanvasExporter>();
builder.Services.AddScoped<IClubJoinRequestService, HttpClubJoinRequestService>();
builder.Services.AddScoped<IClubMemberService, HttpClubMemberService>();
builder.Services.AddScoped<IPlayerService, HttpPlayerService>();
builder.Services.AddScoped<IPlayerIntakeContextService, HttpPlayerIntakeContextService>();
builder.Services.AddPlayerIntakeInterop();
builder.Services.AddScoped<IPlayerLifecycleService, HttpPlayerLifecycleService>();
builder.Services.AddScoped<IPlayerManagementService, HttpPlayerManagementService>();
builder.Services.AddScoped<IPlayerImportService, HttpPlayerImportService>();
builder.Services.AddScoped<ITeamManagementService, HttpTeamManagementService>();
builder.Services.AddScoped<ITeamLifecycleService, HttpTeamLifecycleService>();
builder.Services.AddScoped<ITeamRosterService, HttpTeamRosterService>();
builder.Services.AddScoped<ITeamDetailService, HttpTeamDetailService>();
builder.Services.AddScoped<IPlayerDetailService, HttpPlayerDetailService>();
builder.Services.AddScoped<ICampaignCreationService, HttpCampaignCreationService>();
builder.Services.AddScoped<ICampaignQueryService, HttpCampaignQueryService>();
builder.Services.AddScoped<ICampaignParticipantQueryService, HttpCampaignParticipantQueryService>();
builder.Services.AddScoped<ICampaignPlacementQueryService, HttpCampaignPlacementQueryService>();
builder.Services.AddScoped<IEffectivePlacementQueryService, HttpEffectivePlacementQueryService>();
builder.Services.AddScoped<ICampaignCloseoutQueryService, HttpCampaignCloseoutQueryService>();
builder.Services.AddScoped<IDashboardQueryService, HttpDashboardQueryService>();
builder.Services.AddScoped<IClubActivityQueryService, HttpClubActivityQueryService>();
builder.Services.AddScoped<IClubAttentionQueryService, HttpClubAttentionQueryService>();
builder.Services.AddScoped<ICampaignMetadataService, HttpCampaignMetadataService>();
builder.Services.AddScoped<ISeasonCommandService, HttpSeasonCommandService>();
builder.Services.AddScoped<ISeasonQueryService, HttpSeasonQueryService>();
builder.Services.AddScoped<ICampaignTagApplicationService, HttpCampaignTagApplicationService>();
builder.Services.AddScoped<ICampaignEvaluationNoteService, HttpCampaignEvaluationNoteService>();
builder.Services.AddScoped<ICampaignEvaluationQueryService, HttpCampaignEvaluationQueryService>();
builder.Services.AddScoped<ICampaignPlacementService, HttpCampaignPlacementService>();
builder.Services.AddScoped<IPlacementContextQueryService, HttpPlacementContextQueryService>();
builder.Services.AddScoped<ICampaignLifecycleService, HttpCampaignLifecycleService>();
builder.Services.AddScoped<ITagDefinitionService, HttpTagDefinitionService>();
builder.Services.AddScoped<ITagDefinitionQueryService, HttpTagDefinitionQueryService>();
builder.Services.AddScoped<ITagDefinitionLifecycleService, HttpTagDefinitionLifecycleService>();

await builder.Build().RunAsync();
