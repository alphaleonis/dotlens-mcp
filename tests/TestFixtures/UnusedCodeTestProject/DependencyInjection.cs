using Microsoft.AspNetCore.Http;

namespace UnusedCodeTestProject;

internal interface IClock
{
   DateTime Now();
}

internal sealed class SystemClock : IClock
{
   private readonly TimeSpan _offset;

   public SystemClock()
   {
      _offset = LoadOffset();
   }

   public DateTime Now() => DateTime.UtcNow + _offset;

   private static TimeSpan LoadOffset() => TimeSpan.Zero;
}

internal sealed class TimingMiddleware
{
   private readonly RequestDelegate _next;

   public TimingMiddleware(RequestDelegate next)
   {
      _next = next;
   }

   public async Task InvokeAsync(HttpContext context)
   {
      Stamp(context);
      await _next(context);
   }

   private static void Stamp(HttpContext context) => context.Items["start"] = 0;
}

internal sealed class Plugin
{
   public Plugin()
   {
      Load();
   }

   private void Load()
   {
   }
}

internal sealed class UnregisteredService
{
   public UnregisteredService()
   {
      Prepare();
   }

   private void Prepare()
   {
   }
}
