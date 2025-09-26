using PutDoc.Services;

var builder = WebApplication.CreateBuilder(args);

// Add: command-line + env support already merges into Configuration
// In PutDocStore ctor you already read cfg["PutDocRootPath"].
// Also read an env var PUTDOC_ROOT if present:
builder.Configuration.AddEnvironmentVariables(prefix: "PUTDOC_");

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddSingleton<IAngleSoftFilter, AngleSoftFilter>();
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

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
