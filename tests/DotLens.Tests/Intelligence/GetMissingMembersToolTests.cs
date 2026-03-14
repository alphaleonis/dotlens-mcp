using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetMissingMembersToolTests : TestFixtureTestBase
{
   public GetMissingMembersToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetMissingMembersTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetMissingMembersTool(Workspace, symbolLookup);
   }

   private static LookupByPosition CreateLookup(string filePath, int line, int column)
      => new() { FilePath = filePath, Line = line, Column = column };

   [Fact]
   public async Task GetMissingMembers_OnFullyImplementedClass_ReturnsZeroCounts()
   {
      var filePath = GetCoreTestProjectPath("Types/Interfaces.cs");
      // Line 96: "public class BasicServiceImpl : IBasicService" (1-based coordinates)
      var result = await _tool.GetMissingMembersAsync(CreateLookup(filePath, 96, 14), cancellationToken: CT);

      AssertSuccess(result);
      Assert.Equal(0, result.Data!.InterfaceMemberCount);
      Assert.Equal(0, result.Data.AbstractMemberCount);
   }

   [Fact]
   public async Task GetMissingMembers_FileNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMissingMembersAsync(CreateLookup("NonExistent/file.cs", 10, 10), cancellationToken: CT),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task GetMissingMembers_WithOverriddenAbstractProperty_DoesNotReportAsMissing()
   {
      // ConcreteClass overrides both AbstractMethod and AbstractProperty from AbstractBase
      var filePath = GetCoreTestProjectPath("Types/AbstractAndVirtual.cs");
      // Line 40: "public class ConcreteClass : AbstractBase"
      var result = await _tool.GetMissingMembersAsync(CreateLookup(filePath, 40, 14), cancellationToken: CT);

      AssertSuccess(result);
      Assert.Equal(0, result.Data!.InterfaceMemberCount);
      Assert.Equal(0, result.Data.AbstractMemberCount);
   }

   [Fact]
   public async Task GetMissingMembers_ByTypeName_ReturnsResult()
   {
      // Test name-based lookup
      var result = await _tool.GetMissingMembersAsync(
         new LookupByName { Name = "BasicServiceImpl" },
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.Equal("BasicServiceImpl", result.Data!.TypeName);
   }

   [Fact]
   public async Task GetMissingMembers_SummaryVerbosity_ReturnsCountsWithoutDetails()
   {
      // Use IncompleteServiceImpl which has missing members
      var result = await _tool.GetMissingMembersAsync(
         new LookupByName { Name = "IncompleteServiceImpl" },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      // Should have missing members (ExecuteAsync and Calculate)
      Assert.Equal(2, result.Data!.InterfaceMemberCount);
      Assert.Equal(0, result.Data.AbstractMemberCount);
      // Lists should be null at summary verbosity
      Assert.Null(result.Data.InterfaceMembers);
      Assert.Null(result.Data.AbstractMembers);
   }

   [Fact]
   public async Task GetMissingMembers_FullVerbosity_IncludesSignatures()
   {
      // Use IncompleteServiceImpl which has missing members
      var result = await _tool.GetMissingMembersAsync(
         new LookupByName { Name = "IncompleteServiceImpl" },
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      // IncompleteServiceImpl is missing ExecuteAsync and Calculate from IBasicService
      Assert.Equal(2, result.Data!.InterfaceMemberCount);
      var memberNames = result.Data.InterfaceMembers!.Select(m => m.Name).ToList();
      Assert.Equivalent(new[] { "ExecuteAsync", "Calculate" }, memberNames);
      // All members should have signatures at Full verbosity
      foreach (var member in result.Data.InterfaceMembers)
      {
         Assert.NotNull(member.Signature);
      }
   }

   [Fact]
   public async Task GetMissingMembers_CompactVerbosity_NoSignatures()
   {
      // Use IncompleteServiceImpl which has missing members
      var result = await _tool.GetMissingMembersAsync(
         new LookupByName { Name = "IncompleteServiceImpl" },
         verbosity: ToolResultVerbosity.Compact,
         cancellationToken: CT);

      AssertSuccess(result);
      // IncompleteServiceImpl is missing ExecuteAsync and Calculate from IBasicService
      Assert.Equal(2, result.Data!.InterfaceMemberCount);
      var memberNames = result.Data.InterfaceMembers!.Select(m => m.Name).ToList();
      Assert.Equivalent(new[] { "ExecuteAsync", "Calculate" }, memberNames);
      // All members should NOT have signatures at Compact verbosity
      foreach (var member in result.Data.InterfaceMembers)
      {
         Assert.Null(member.Signature);
      }
   }

   [Fact]
   public async Task GetMissingMembers_OnNonTypePosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Point to an empty area or method, not a type declaration
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMissingMembersAsync(CreateLookup(filePath, 1, 1), cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }
}
