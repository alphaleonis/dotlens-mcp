using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Search;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers the member lists of types whose bases sit in other projects or declare operators, the kinds of nested types,
/// the member kind filter, and the base lists of records.
/// </summary>
public class MemberListsFixtureTests : IClassFixture<MemberListsFixtureWorkspace>
{
   private readonly MemberListsFixtureWorkspace _fixture;

   public MemberListsFixtureTests(MemberListsFixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   /// <summary>
   /// Ledger's project gives no access to its internals, so its internal and private protected members stay out.
   /// </summary>
   [Fact]
   public async Task TypeMembers_IncludeInherited_Accessibility_BaseInOtherProject()
   {
      var members = await InheritedMembersOfAsync("Account");

      Assert.Equal(
         [
            ("MemberListsTestProject.Account", "Deposit", "Method", 8),
            ("MemberListsBaseLibrary.Ledger", "Post", "Method", 5),
            ("MemberListsBaseLibrary.Ledger", "Audit", "Method", 7),
            ("MemberListsBaseLibrary.Ledger", "Close", "Method", 11),
         ],
         members);
   }

   [Fact]
   public async Task TypeMembers_IncludeInherited_Accessibility_BaseInFriendProject()
   {
      var members = await InheritedMembersOfAsync("Diary");

      Assert.Equal(
         [
            ("MemberListsTestProject.Diary", "Read", "Method", 13),
            ("MemberListsFriendLibrary.Journal", "Write", "Method", 5),
            ("MemberListsFriendLibrary.Journal", "Balance", "Method", 7),
            ("MemberListsFriendLibrary.Journal", "Lock", "Method", 9),
         ],
         members);
   }

   /// <summary>
   /// A class nested in its base can access the base's private members.
   /// </summary>
   [Fact]
   public async Task TypeMembers_IncludeInherited_Accessibility_NestedDerivedType()
   {
      var members = await InheritedMembersOfAsync("Vault.Inner");

      Assert.Equal(
         [
            ("MemberListsTestProject.Vault", "_code", "Field", 18),
            ("MemberListsTestProject.Vault", "Code", "Property", 20),
            ("MemberListsTestProject.Vault", "get_Code", "Accessor", 20),
            ("MemberListsTestProject.Vault", "Inner", "Class", 22),
         ],
         members);
   }

   /// <summary>
   /// Operators of a base class are inherited like its other members, and an operator cannot hide one of the base.
   /// </summary>
   [Fact]
   public async Task TypeMembers_IncludeInherited_Operators()
   {
      var members = await InheritedMembersOfAsync("Wage");

      Assert.Equal(
         [
            ("MemberListsTestProject.Wage", "Hours", "Field", 22),
            ("MemberListsTestProject.Wage", "op_Addition", "Operator", 24),
            ("MemberListsTestProject.Money", "Amount", "Field", 5),
            ("MemberListsTestProject.Money", "op_Addition", "Operator", 7),
            ("MemberListsTestProject.Money", "op_True", "Operator", 9),
            ("MemberListsTestProject.Money", "op_False", "Operator", 11),
            ("MemberListsTestProject.Money", "op_Implicit", "Conversion", 13),
            ("MemberListsTestProject.Money", "op_Explicit", "Conversion", 15),
            ("MemberListsTestProject.Money", "op_AdditionAssignment", "Operator", 17),
         ],
         members);
   }

   [Fact]
   public async Task TypeMembers_MemberKinds_CountsMatchFilters()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "Gadget" }, cancellationToken: CT);

      Assert.Equal(
         [
            (".cctor", "Constructor"),
            (".ctor", "Constructor"),
            ("Finalize", "Destructor"),
            ("Size", "Property"),
            ("get_Size", "Accessor"),
            ("set_Size", "Accessor"),
            ("Changed", "Event"),
            ("Weight", "Field"),
            ("Run", "Method"),
            ("System.IDisposable.Dispose", "Method"),
            ("op_UnaryNegation", "Operator"),
            ("op_Explicit", "Conversion"),
         ],
         result.Data.Members.Select(m => (m.Name, m.Kind)));
      Assert.Equal(
         new Dictionary<string, int>
         {
            ["Constructor"] = 2,
            ["Destructor"] = 1,
            ["Property"] = 1,
            ["Accessor"] = 2,
            ["Event"] = 1,
            ["Field"] = 1,
            ["Method"] = 2,
            ["Operator"] = 1,
            ["Conversion"] = 1,
         },
         result.Data.MemberCounts);
      var filtersWithoutMembers = new List<MemberKindFilter>();
      foreach (var filter in Enum.GetValues<MemberKindFilter>())
      {
         var matched = new List<string>();
         foreach (var typeName in (string[])["Gadget", "Quay"])
         {
            var all = await tool.GetTypeMembersAsync(new LookupByName { Name = typeName }, cancellationToken: CT);
            var filtered = await tool.GetTypeMembersAsync(new LookupByName { Name = typeName }, memberKind: filter, cancellationToken: CT);
            var batchFiltered = await batch.GetTypeMembersBatchAsync([typeName], memberKind: filter, cancellationToken: CT);

            var expected = all.Data.Members.Where(m => IsReportedAs(filter, m.Kind)).ToList();
            Assert.Equal(expected.Select(m => m.Name), filtered.Data.Members.Select(m => m.Name));
            Assert.Equal(expected.CountBy(m => m.Kind).ToDictionary(), filtered.Data.MemberCounts);
            Assert.Equal(expected.Select(m => m.Name), Assert.Single(batchFiltered.Data.Results).Members.Select(m => m.Name));
            matched.AddRange(expected.Select(m => m.Name));
         }

         if (matched.Count == 0)
            filtersWithoutMembers.Add(filter);
      }

      Assert.Equal([], filtersWithoutMembers);
   }

   /// <summary>
   /// An explicit implementation takes the kind of the member it implements, in the member list and in the overview.
   /// </summary>
   [Fact]
   public async Task TypeMembers_MemberKinds_ExplicitImplementations()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var overview = new GetTypeOverviewTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "Tally" }, cancellationToken: CT);
      var methods = await tool.GetTypeMembersAsync(new LookupByName { Name = "Tally" }, memberKind: MemberKindFilter.Method, cancellationToken: CT);
      var overviewResult = await overview.GetTypeOverviewAsync(new LookupByName { Name = "Tally" }, cancellationToken: CT);

      Assert.Equal(
         [
            ("Value", "Field"),
            ("MemberListsTestProject.IAddable<MemberListsTestProject.Tally>.op_Addition", "Operator"),
            ("MemberListsTestProject.IAddable<MemberListsTestProject.Tally>.op_Explicit", "Conversion"),
            ("MemberListsTestProject.IAddable<MemberListsTestProject.Tally>.Reset", "Method"),
            ("op_Subtraction", "Operator"),
            ("Twice", "Method"),
         ],
         result.Data.Members.Select(m => (m.Name, m.Kind)));
      Assert.Equal(["MemberListsTestProject.IAddable<MemberListsTestProject.Tally>.Reset", "Twice"], methods.Data.Members.Select(m => m.Name));
      Assert.Equal(2, overviewResult.Data.MemberCounts.Methods);
   }

   [Fact]
   public async Task TypeMembers_NestedTypeKinds()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "Harbor" }, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(["Harbor"], cancellationToken: CT);

      (string, string, int?)[] expected =
      [
         ("Dock", "Class", 5),
         ("Berth", "Struct", 7),
         ("IMooring", "Interface", 9),
         ("Tide", "Enum", 11),
         ("Signal", "Delegate", 13),
         ("Manifest", "Class", 15),
         ("Cargo", "Struct", 17),
      ];
      Assert.Equal(expected, result.Data.Members.Select(m => (m.Name, m.Kind, m.Line)));
      Assert.Equal(
         new Dictionary<string, int> { ["Class"] = 2, ["Struct"] = 2, ["Interface"] = 1, ["Enum"] = 1, ["Delegate"] = 1 },
         result.Data.MemberCounts);
      Assert.Equal(7, result.Data.TotalMembers);
      Assert.Equal(expected, Assert.Single(batchResult.Data.Results).Members.Select(m => (m.Name, m.Kind, m.Line)));

      var nested = await tool.GetTypeMembersAsync(new LookupByName { Name = "Harbor" }, memberKind: MemberKindFilter.NestedType, cancellationToken: CT);
      Assert.Equal(expected, nested.Data.Members.Select(m => (m.Name, m.Kind, m.Line)));
   }

   /// <summary>
   /// Quay declares a field and a method beside its nested types, and an extension block, which counts as a nested type.
   /// </summary>
   [Fact]
   public async Task TypeMembers_NestedTypeKinds_FilterLeavesOutOtherMembers()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var all = await tool.GetTypeMembersAsync(new LookupByName { Name = "Quay" }, cancellationToken: CT);
      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "Quay" }, memberKind: MemberKindFilter.NestedType, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(["Quay"], memberKind: MemberKindFilter.NestedType, cancellationToken: CT);

      Assert.Equal(
         [
            ("Depth", "Field", 5),
            ("Crane", "Class", 7),
            ("Bollard", "Struct", 9),
            ("IRope", "Interface", 11),
            ("Swell", "Enum", 13),
            ("Horn", "Delegate", 15),
            ("Ticket", "Struct", 17),
            ("extension(string)", "Extension", 19),
            ("Moor", "Method", 24),
         ],
         all.Data.Members.Select(m => (m.Name, m.Kind, m.Line)));
      (string, string, int?)[] expected =
      [
         ("Crane", "Class", 7),
         ("Bollard", "Struct", 9),
         ("IRope", "Interface", 11),
         ("Swell", "Enum", 13),
         ("Horn", "Delegate", 15),
         ("Ticket", "Struct", 17),
         ("extension(string)", "Extension", 19),
      ];
      Assert.Equal(expected, result.Data.Members.Select(m => (m.Name, m.Kind, m.Line)));
      Assert.Equal(
         new Dictionary<string, int> { ["Class"] = 1, ["Struct"] = 2, ["Interface"] = 1, ["Enum"] = 1, ["Delegate"] = 1, ["Extension"] = 1 },
         result.Data.MemberCounts);
      Assert.Equal(expected, Assert.Single(batchResult.Data.Results).Members.Select(m => (m.Name, m.Kind, m.Line)));
   }

   /// <summary>
   /// One record writes another type's IEquatable, the other writes its own through an alias, each in one of two parts.
   /// </summary>
   [Fact]
   public async Task FullDeclaration_RecordEquatable_PartialRecords()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("RecordEquatables.cs"), ToolResultVerbosity.Full, CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(
         [
            "public record StringEquatableEntry(int Id) : IEquatable<string>, IDisposable",
            "public record StringEquatableEntry(int Id) : IEquatable<string>, IDisposable",
            "public record AliasedEntry(int Id) : IEquatable<AliasedEntry>, IDisposable",
            "public record AliasedEntry(int Id) : IEquatable<AliasedEntry>, IDisposable",
         ],
         types.Select(t => t.Declaration));
   }

   /// <summary>
   /// Returns whether <paramref name="filter"/> selects members of the reported kind. NestedType selects every type kind.
   /// </summary>
   private static bool IsReportedAs(MemberKindFilter filter, string kind) =>
      filter == MemberKindFilter.NestedType ? Enum.GetNames<TypeKind>().Contains(kind) : kind == filter.ToString();

   private async Task<List<(string? ContainingType, string Name, string Kind, int? Line)>> InheritedMembersOfAsync(string typeName)
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeMembersAsync(
         new LookupByName { Name = typeName }, includeInherited: true, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(
         [typeName], includeInherited: true, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);

      var members = result.Data.Members.Select(m => (m.ContainingType, m.Name, m.Kind, m.Line)).ToList();
      Assert.Equal(members, Assert.Single(batchResult.Data.Results).Members.Select(m => (m.ContainingType, m.Name, m.Kind, m.Line)));
      Assert.Equal(members.Count, result.Data.TotalMembers);
      return members;
   }
}
