using Microsoft.AspNetCore.Components.Web;
using PutDoc;
using PutDoc.Components;
using PutDoc.Services;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Configuration.AddEnvironmentVariables(prefix: "PUTDOC_");

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();   // ← enables Blazor Server interactivity

// 👇 enable JS-activated root components
builder.Services.AddServerSideBlazor(options =>
{
    // identifier can be any string you choose
    options.RootComponents.RegisterForJavaScript<InlineToolbar>(
        identifier: "putdoc.toolbar"
        // , javaScriptInitializer: "putdocInit"  // optional initializer hook
    );
}).AddCircuitOptions(options =>
{
    // Enable DetailedErrors only in the Development environment for security reasons
    //if (_env.IsDevelopment())
    {
        options.DetailedErrors = true;
    }
});

builder.Services.AddSingleton<IAngleSoftFilter, AngleSoftFilter>();
builder.Services.AddSingleton<DocVersionService>();
builder.Services.AddSingleton<DocWriterService>();
builder.Services.AddSingleton<DocInvalidationService>();
builder.Services.AddSingleton<PresenceService>(); 
builder.Services.AddSingleton<IPutDocStore, PutDocStore>();
builder.Services.AddScoped<PutDocState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAntiforgery();

// ✅ New endpoint-style hosting (no _Host.cshtml, no MapBlazorHub)
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();