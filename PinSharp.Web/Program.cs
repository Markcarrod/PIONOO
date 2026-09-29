using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using PinSharp.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 1024L * 1024L * 1024L;
});

builder.Services.AddSingleton<BatchRenderService>();
builder.Services.AddRazorPages(options =>
{
    // This is a private batch tool, not a public form. Avoid stale-token 400s after app restarts.
    options.Conventions.ConfigureFilter(new IgnoreAntiforgeryTokenAttribute());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
