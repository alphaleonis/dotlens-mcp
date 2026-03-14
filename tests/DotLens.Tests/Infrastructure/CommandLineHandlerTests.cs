using DotLens.Infrastructure;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class CommandLineHandlerTests
{
   [Fact]
   public void NoArgs_ReturnsRunServer()
   {
      var action = CommandLineHandler.Process([], new StringWriter(), new StringWriter());

      Assert.Equal(StartupAction.RunServer, action);
   }

   [Theory]
   [InlineData("--help")]
   [InlineData("-h")]
   [InlineData("--Help")]
   [InlineData("--HELP")]
   public void HelpFlag_ReturnsExit(string flag)
   {
      var stdout = new StringWriter();

      var action = CommandLineHandler.Process([flag], stdout, new StringWriter());

      Assert.Equal(StartupAction.Exit, action);
      Assert.Contains("USAGE:", stdout.ToString());
   }

   [Theory]
   [InlineData("--version")]
   [InlineData("-v")]
   [InlineData("--Version")]
   [InlineData("--VERSION")]
   public void VersionFlag_ReturnsExit(string flag)
   {
      var stdout = new StringWriter();

      var action = CommandLineHandler.Process([flag], stdout, new StringWriter());

      Assert.Equal(StartupAction.Exit, action);
      Assert.Equal(Program.GetVersion(), stdout.ToString().Trim());
   }

   [Theory]
   [InlineData("--worker")]
   [InlineData("--Worker")]
   public void WorkerFlag_ReturnsRunWorker(string flag)
   {
      var action = CommandLineHandler.Process([flag], new StringWriter(), new StringWriter());

      Assert.Equal(StartupAction.RunWorker, action);
   }

   [Fact]
   public void HelpFlag_TakesPriorityOverWorker()
   {
      var stdout = new StringWriter();

      var action = CommandLineHandler.Process(["--worker", "--help"], stdout, new StringWriter());

      Assert.Equal(StartupAction.Exit, action);
      Assert.Contains("USAGE:", stdout.ToString());
   }

   [Fact]
   public void HelpFlag_TakesPriorityOverVersion()
   {
      var stdout = new StringWriter();

      var action = CommandLineHandler.Process(["--version", "--help"], stdout, new StringWriter());

      Assert.Equal(StartupAction.Exit, action);
      Assert.Contains("USAGE:", stdout.ToString());
   }

   [Theory]
   [InlineData("--LogLevel=Debug")]
   [InlineData("--SolutionPath=C:\\My.sln")]
   [InlineData("--MaxCacheSize=1000")]
   [InlineData("--loglevel=debug")] // case-insensitive
   public void KnownOption_ReturnsRunServer_NoWarnings(string arg)
   {
      var stderr = new StringWriter();

      var action = CommandLineHandler.Process([arg], new StringWriter(), stderr);

      Assert.Equal(StartupAction.RunServer, action);
      Assert.Empty(stderr.ToString());
   }

   [Theory]
   [InlineData("--foo")]
   [InlineData("--bar=baz")]
   [InlineData("-x")]
   public void UnknownArg_ReturnsRunServer_EmitsWarning(string arg)
   {
      var stderr = new StringWriter();

      var action = CommandLineHandler.Process([arg], new StringWriter(), stderr);

      Assert.Equal(StartupAction.RunServer, action);
      Assert.Contains($"warning: unrecognized argument '{arg}'", stderr.ToString());
   }

   [Fact]
   public void PositionalArg_IsIgnored()
   {
      var stderr = new StringWriter();

      var action = CommandLineHandler.Process(["somefile.sln"], new StringWriter(), stderr);

      Assert.Equal(StartupAction.RunServer, action);
      Assert.Empty(stderr.ToString());
   }

   [Fact]
   public void MultipleUnknownArgs_EmitsMultipleWarnings()
   {
      var stderr = new StringWriter();

      var action = CommandLineHandler.Process(["--foo", "--bar"], new StringWriter(), stderr);

      Assert.Equal(StartupAction.RunServer, action);

      var lines = stderr.ToString().TrimEnd().Split(Environment.NewLine);
      Assert.Equal(2, lines.Length);
      Assert.Equal("warning: unrecognized argument '--foo'", lines[0]);
      Assert.Equal("warning: unrecognized argument '--bar'", lines[1]);
   }

   [Fact]
   public void HelpText_ContainsVersion()
   {
      var helpText = CommandLineHandler.GetHelpText();

      Assert.Contains($"DotLens v{Program.GetVersion()}", helpText);
   }

   [Fact]
   public void HelpText_ContainsAllOptionNames()
   {
      var helpText = CommandLineHandler.GetHelpText();
      var properties = typeof(DotLensOptions)
         .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
      Assert.NotEmpty(properties);

      foreach (var prop in properties)
      {
         Assert.Contains($"--{prop.Name}", helpText);
      }
   }

   [Fact]
   public void HelpText_ContainsDescriptions()
   {
      var helpText = CommandLineHandler.GetHelpText();

      // Spot-check a few descriptions from [Description] attributes
      Assert.Contains("Server instructions level", helpText);
      Assert.Contains("Log level", helpText);
      Assert.Contains("Solution path", helpText);
      Assert.Contains("Auto-detect and load solution", helpText);
   }

   [Fact]
   public void HelpText_ContainsDefaultValues()
   {
      var helpText = CommandLineHandler.GetHelpText();

      Assert.Contains("Detailed", helpText); // InstructionLevel default
      Assert.Contains("Warning", helpText); // LogLevel default
      Assert.Contains("(none)", helpText); // SolutionPath default (null)
      Assert.Contains("100", helpText); // MaxDiagnostics default
   }

   [Fact]
   public void HelpText_ContainsSections()
   {
      var helpText = CommandLineHandler.GetHelpText();

      Assert.Contains("USAGE:", helpText);
      Assert.Contains("FLAGS:", helpText);
      Assert.Contains("OPTIONS:", helpText);
      Assert.Contains("EXAMPLES:", helpText);
   }
}
