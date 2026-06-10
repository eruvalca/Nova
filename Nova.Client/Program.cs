using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nova.Client.Telemetry;

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

await builder.Build().RunAsync();
