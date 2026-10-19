using AtomUpdate.Web.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AtomBlazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAtomBlazorComponents();
builder.Services.AddAdminClientServices(builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();
