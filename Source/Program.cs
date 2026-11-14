using System.Diagnostics;
using System.Text.Json;
using RedMeridian.Domain;
using RedMeridian.Application;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5274");
builder.Services.AddSingleton(new GameRules(Path.Combine(builder.Environment.ContentRootPath, "Config")));
builder.Services.AddSingleton<ColonySession>();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});
app.MapGet("/api/catalog", (GameRules rules) => new { rules.Buildings, rules.Materials, rules.Research, rules.World, rules.ShipTypes,
    landingSite = rules.NewSite(ColonySite.Mars) });
app.MapGet("/api/saves", (ColonySession session) => session.Saves());
app.MapGet("/api/state", (int? inspect, ColonySession session) => session.Read(inspect ?? -1));
app.MapGet("/api/surface", (HttpContext context, ColonySession session) => session.Surface(context));
app.MapGet("/api/map", (HttpContext context, ColonySession session) => session.Map(context));
app.MapGet("/api/map-resources", (string mapId, ColonySession session) => session.MapResources(mapId));
app.MapGet("/api/climate", (HttpContext context, ColonySession session) => session.Climate(context));
app.MapGet("/api/logistics", (ColonySession session) => session.Logistics());
app.MapPost("/api/command", (Command command, ColonySession session) => session.Apply(command));
app.MapPost("/api/import", ImportColony);
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
var ticks = TickLoop(app.Services.GetRequiredService<ColonySession>(), app.Lifetime.ApplicationStopping);
await app.RunAsync();
await ticks;

static async Task<IResult> ImportColony(HttpContext context, int revision, ColonySession session)
{
    var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = SaveCodec.MaxUploadBytes;
    if (context.Request.ContentLength > SaveCodec.MaxUploadBytes)
        return Results.BadRequest(new { message = "Save file exceeds 64 MiB." });
    try
    {
        using var file = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer,context.RequestAborted)) != 0)
        {
            if (file.Length + count > SaveCodec.MaxUploadBytes)
                return Results.BadRequest(new { message = "Save file exceeds 64 MiB." });
            file.Write(buffer,0,count);
        }
        file.Position = 0;
        var save = SaveCodec.Read(file,limitExpandedSize: true);
        return session.Apply(new("import",Revision: revision,Save: save));
    }
    catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        return Results.BadRequest(new { message = "Save file exceeds 64 MiB." });
    }
    catch (Exception error) when (error is JsonException or InvalidDataException)
    {
        return Results.BadRequest(new { message = "Invalid colony save. " + error.Message });
    }
}

async Task TickLoop(ColonySession session, CancellationToken cancellation)
{
    try
    {
        var elapsed = Stopwatch.StartNew();
        while (await timer.WaitForNextTickAsync(cancellation))
        {
            session.Advance(elapsed.Elapsed.TotalSeconds);
            elapsed.Restart();
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
}


