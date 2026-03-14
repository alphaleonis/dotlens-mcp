using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

public class AddNullChecksToolTests : IsolatedProjectTestBase
{

   private AddNullChecksTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      var symbolLookupService = new SymbolLookupService(WorkspaceManager);
      _tool = new AddNullChecksTool(WorkspaceManager, symbolLookupService);
   }

   #region Position Mode Tests

   [Fact]
   public async Task AddNullChecks_PositionMode_ValidMethod_ProcessesRequest()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Line 120: "public static string? ToUpperOrNull(this string? input)" - has nullable param
      var target = new LookupByPosition { FilePath = filePath, Line = 120, Column = 4 };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
   }

   [Fact]
   public async Task AddNullChecks_PositionMode_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new LookupByPosition { FilePath = filePath, Line = 99999, Column = 1 };
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task AddNullChecks_PositionMode_FileNotFound_ThrowsException()
   {
      var target = new LookupByPosition { FilePath = "NonExistent/file.cs", Line = 1, Column = 1 };
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task AddNullChecks_PositionMode_NonMethodPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Position on empty line (not a method) - line 4 is empty
      var target = new LookupByPosition { FilePath = filePath, Line = 4, Column = 1 };
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   #endregion

   #region Symbol Mode Tests

   [Fact]
   public async Task AddNullChecks_SymbolMode_ValidMethod_Succeeds()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NullableParam" };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("NullableParam", data.GetStringOrNull("methodName"));
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
   }

   [Fact]
   public async Task AddNullChecks_SymbolMode_TypeNotFound_ThrowsToolException()
   {
      var target = new LookupByName { Name = "NonExistentType.SomeMethod" };
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   [Fact]
   public async Task AddNullChecks_SymbolMode_MethodNotFound_ThrowsToolException()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NonExistentMethod" };
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   #endregion

   #region Verbosity Tests

   [Fact]
   public async Task AddNullChecks_VerbositySummary_ReturnsOnlyCount()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NullableParam" };
      var result = await _tool.AddNullChecksAsync(target,
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
      Assert.Null(data["parameters"]);  // Not included in summary
      Assert.Null(data["guardCode"]);   // Not included in summary
   }

   [Fact]
   public async Task AddNullChecks_VerbosityCompact_ReturnsNamesAndTypes()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NullableParam" };
      var result = await _tool.AddNullChecksAsync(target,
         verbosity: ToolResultVerbosity.Compact,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      Assert.Single(parameters!);
      Assert.Null(data["guardCode"]);  // Not included in compact
   }

   [Fact]
   public async Task AddNullChecks_VerbosityFull_IncludesGuardCode()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NullableParam" };
      var result = await _tool.AddNullChecksAsync(target,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      var guardCode = data.GetStringOrNull("guardCode");
      Assert.NotNull(guardCode);
      Assert.Contains("System.ArgumentNullException", guardCode);
   }

   #endregion

   #region ExcludeParams Tests

   [Fact]
   public async Task AddNullChecks_ExcludeParams_CommaSeparated_ExcludesSpecified()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.MixedParams" };
      var result = await _tool.AddNullChecksAsync(target,
         includeNonNullable: true,
         excludeParams: ["nonNullable"],
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Only nullable param included (nonNullable excluded)
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      Assert.Single(parameters!);
      Assert.Equal("nullable", parameters[0]?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task AddNullChecks_ExcludeParams_JsonArray_ExcludesSpecified()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.MixedParams" };
      var result = await _tool.AddNullChecksAsync(target,
         includeNonNullable: true,
         excludeParams: ["nullable"],
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Only nonNullable param included (nullable excluded)
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      Assert.Single(parameters!);
      Assert.Equal("nonNullable", parameters[0]?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task AddNullChecks_ExcludeParams_AllExcluded_ThrowsWithHint()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NullableParam" };
      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target,
            excludeParams: ["value"],
            cancellationToken: CT),
         "ANALYSIS_FAILED");

      Assert.Contains("excluded", ex.Hint, StringComparison.OrdinalIgnoreCase);
   }

   #endregion

   #region IncludeNonNullable Tests

   [Fact]
   public async Task AddNullChecks_NonNullableParam_DefaultBehavior_ThrowsWithHint()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NonNullableParam" };
      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "ANALYSIS_FAILED");

      Assert.Contains("includeNonNullable=true", ex.Hint);
   }

   [Fact]
   public async Task AddNullChecks_NonNullableParam_IncludeNonNullableTrue_Succeeds()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.NonNullableParam" };
      var result = await _tool.AddNullChecksAsync(target,
         includeNonNullable: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal("NonNullableParam", data.GetStringOrNull("methodName"));
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));

      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      Assert.Single(parameters!);
      Assert.Equal("value", parameters[0]?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task AddNullChecks_MixedParams_DefaultBehavior_OnlyNullable()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.MixedParams" };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Default behavior: only nullable parameter included
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));

      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      Assert.Single(parameters!);
      Assert.Equal("nullable", parameters[0]?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task AddNullChecks_MixedParams_IncludeNonNullableTrue_BothParams()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.MixedParams" };
      var result = await _tool.AddNullChecksAsync(target,
         includeNonNullable: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // includeNonNullable=true: both parameters included
      Assert.Equal(2, data.GetIntOrNull("parameterCount"));

      var parameters = data.GetArrayOrNull("parameters");
      Assert.NotNull(parameters);
      Assert.Equal(2, parameters!.Count);
      Assert.Equal("nonNullable", parameters[0]?.GetStringOrNull("name"));
      Assert.Equal("nullable", parameters[1]?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task AddNullChecks_ValueTypeOnly_BothModes_Throws()
   {
      var target = new LookupByName { Name = "NullCheckTestClass.ValueTypeOnly" };

      // Default mode
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "ANALYSIS_FAILED");

      // includeNonNullable mode - still fails because no reference types
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, includeNonNullable: true, cancellationToken: CT),
         "ANALYSIS_FAILED");
   }

   #endregion

   #region Method Kind Tests

   [Fact]
   public async Task AddNullChecks_Constructor_Succeeds()
   {
      // Line 12: public NullCheckMethodKinds(string? value)
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 12,
         Column = 10
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(".ctor", data.GetStringOrNull("methodName"));
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
   }

   [Fact]
   public async Task AddNullChecks_Operator_Succeeds()
   {
      // Line 24: public static NullCheckMethodKinds operator +(...)
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 24,
         Column = 10
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("op_Addition", data.GetStringOrNull("methodName"));
   }

   [Fact]
   public async Task AddNullChecks_PropertySetter_Succeeds()
   {
      // Line 33: set { _ = value; }
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 33,
         Column = 10
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("set_Name", data.GetStringOrNull("methodName"));
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
   }

   [Fact]
   public async Task AddNullChecks_IndexerSetter_Succeeds()
   {
      // Line 40: set { _ = key; _ = value; }
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 40,
         Column = 10
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("set_Item", data.GetStringOrNull("methodName"));
      // Indexer setter has both 'key' and 'value' params
      Assert.Equal(2, data.GetIntOrNull("parameterCount"));
   }

   [Fact]
   public async Task AddNullChecks_EventAccessor_Succeeds()
   {
      // Line 47: add { _myEvent += value; }
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 47,
         Column = 10
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("add_MyEvent", data.GetStringOrNull("methodName"));
      Assert.Equal(1, data.GetIntOrNull("parameterCount"));
   }

   [Fact]
   public async Task AddNullChecks_StaticMethod_Succeeds()
   {
      // Line 52: public static void StaticMethod(string? value)
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 52,
         Column = 10
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("StaticMethod", data.GetStringOrNull("methodName"));
   }

   [Fact]
   public async Task AddNullChecks_LocalFunction_Succeeds()
   {
      // Line 62: void LocalFunc(string? param)
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 62,
         Column = 15
      };
      var result = await _tool.AddNullChecksAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("LocalFunc", data.GetStringOrNull("methodName"));
   }

   [Fact]
   public async Task AddNullChecks_ExpressionBodied_ThrowsToolException()
   {
      // Line 69: public string ExpressionBodied(...) => ...
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Refactoring/NullCheckMethodKinds.cs"),
         Line = 69,
         Column = 10
      };

      await AssertThrowsToolExceptionAsync(
         () => _tool.AddNullChecksAsync(target, cancellationToken: CT),
         "ANALYSIS_FAILED");
   }

   #endregion
}
