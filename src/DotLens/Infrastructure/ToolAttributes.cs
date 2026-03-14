namespace DotLens.Infrastructure;

/// <summary>
/// Marks a tool class for registration in worker mode.
/// Worker tools run in the worker process with direct access to MSBuildWorkspace.
/// In server mode, these tools are forwarded to the worker process.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class DotLensWorkerToolAttribute : Attribute
{
   /// <summary>
   /// When true, this tool is for internal server-to-worker communication only
   /// and will not be forwarded/exposed to MCP clients.
   /// </summary>
   public bool Internal { get; set; }
}

/// <summary>
/// Marks a tool class for registration in server mode only.
/// Server tools manage worker lifecycles and execute directly in the server process.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class DotLensServerToolAttribute : Attribute { }
