using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using UnusedCodeTestProject;

Startup.Run();
HandlerScan.Run();
TypeArgumentUses.Run();
HandoverShapeUses.Run();
ArgumentUses.Run();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IClock, SystemClock>();
var app = builder.Build();
app.UseMiddleware<TimingMiddleware>();
app.Services.GetRequiredService<IClock>().Now();
Activator.CreateInstance(typeof(Plugin));

using var handle = new Handle();
Console.WriteLine($"{handle}");
