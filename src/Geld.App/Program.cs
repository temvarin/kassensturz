using Geld.App;
using Geld.App.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after"); // the tab title set by each page's TopBar
builder.Services.AddAppServices();

await builder.Build().RunAsync();
