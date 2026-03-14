using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Tools.Intelligence;
using FakeItEasy;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests batch operations with partial failures - some items succeed, some fail.
/// </summary>
public class BatchOperationTests : MockableRoslynServiceTestBase
{
   private GetTypeMembersBatchTool _getTypeMembersBatch = null!;

   public BatchOperationTests()
   {
      SetupSolutionLoaded();
      _getTypeMembersBatch = new GetTypeMembersBatchTool(FakeWorkspaceManager);
   }

   /// <summary>
   /// Helper to create a TypeLookupResult for testing.
   /// The batch tools only use the Type property, so Compilation/Project can be null.
   /// </summary>
   private static TypeLookupResult CreateTypeLookupResult(INamedTypeSymbol type) =>
      new()
      {
         Type = type,
         Compilation = null!, // Not used by batch tools
         Project = null!      // Not used by batch tools
      };

   /// <summary>
   /// Helper to check success - with ToolResult&lt;T&gt;, success is indicated by presence of data property.
   /// Errors throw ToolException, so if we have a result, it's successful.
   /// </summary>
   private static bool IsSuccess(JsonNode json) =>
      json["data"] != null || json["Data"] != null;

   /// <summary>
   /// Helper to get data across old and new formats.
   /// </summary>
   private static JsonNode GetData(JsonNode json) =>
      json["data"] ?? json["Data"] ?? throw new InvalidOperationException("No data found");

   #region GetTypeMembersBatchAsync Tests

   [Fact]
   public async Task GetTypeMembersBatch_MixedValidAndInvalid_ReturnsPartialResults()
   {
      // Arrange: ValidType exists, InvalidType does not
      var mockType = A.Fake<INamedTypeSymbol>();
      A.CallTo(() => mockType.ToDisplayString()).Returns("Test.ValidType");
      A.CallTo(() => mockType.GetMembers()).Returns([]);

      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync("ValidType", A<bool>._, A<string?>._, A<CancellationToken>._))
         .Returns(Task.FromResult<TypeLookupResult?>(CreateTypeLookupResult(mockType)));
      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync("InvalidType", A<bool>._, A<string?>._, A<CancellationToken>._))
         .Returns(Task.FromResult<TypeLookupResult?>(null));

      // Act
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(
         ["ValidType", "InvalidType"],
         cancellationToken: CT);

      // Assert
      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.True(IsSuccess(json));

      var data = GetData(json);
      Assert.Equal(2, data.GetIntOrNull("requested") ?? data.GetIntOrNull("Requested"));
      Assert.Equal(1, data.GetIntOrNull("succeeded") ?? data.GetIntOrNull("Succeeded"));
      Assert.Equal(1, data.GetIntOrNull("failed") ?? data.GetIntOrNull("Failed"));

      // Check results array has valid type
      var results = data.GetArrayOrNull("results") ?? data.GetArrayOrNull("Results");
      Assert.NotNull(results);
      Assert.Single(results);
      Assert.Equal("Test.ValidType", results[0]?.GetStringOrNull("typeName") ?? results[0]?.GetStringOrNull("TypeName"));

      // Check errors array has invalid type
      var errors = data.GetArrayOrNull("errors") ?? data.GetArrayOrNull("Errors");
      Assert.NotNull(errors);
      Assert.Single(errors);
      Assert.Equal("InvalidType", errors[0]?.GetStringOrNull("typeName") ?? errors[0]?.GetStringOrNull("TypeName"));
      var errorMsg = errors[0]?.GetStringOrNull("error") ?? errors[0]?.GetStringOrNull("Error");
      Assert.Contains("not found", errorMsg, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task GetTypeMembersBatch_AllInvalid_ReturnsAllErrors()
   {
      // Arrange: All types are invalid
      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync(A<string>._, A<bool>._, A<string?>._, A<CancellationToken>._))
         .Returns(Task.FromResult<TypeLookupResult?>(null));

      // Act
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(
         ["NonExistent1", "NonExistent2", "NonExistent3"],
         cancellationToken: CT);

      // Assert
      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.True(IsSuccess(json)); // Batch itself succeeds

      var data = GetData(json);
      Assert.Equal(3, data.GetIntOrNull("requested") ?? data.GetIntOrNull("Requested"));
      Assert.Equal(0, data.GetIntOrNull("succeeded") ?? data.GetIntOrNull("Succeeded"));
      Assert.Equal(3, data.GetIntOrNull("failed") ?? data.GetIntOrNull("Failed"));

      var results = data.GetArrayOrNull("results") ?? data.GetArrayOrNull("Results");
      Assert.NotNull(results);
      Assert.Empty(results);

      var errors = data.GetArrayOrNull("errors") ?? data.GetArrayOrNull("Errors");
      Assert.NotNull(errors);
      Assert.Equal(3, errors.Count);
   }

   [Fact]
   public async Task GetTypeMembersBatch_AllValid_ReturnsAllResults()
   {
      // Arrange: All types are valid
      var mockType1 = A.Fake<INamedTypeSymbol>();
      A.CallTo(() => mockType1.ToDisplayString()).Returns("Type1");
      A.CallTo(() => mockType1.GetMembers()).Returns([]);

      var mockType2 = A.Fake<INamedTypeSymbol>();
      A.CallTo(() => mockType2.ToDisplayString()).Returns("Type2");
      A.CallTo(() => mockType2.GetMembers()).Returns([]);

      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync("Type1", A<bool>._, A<string?>._, A<CancellationToken>._))
         .Returns(Task.FromResult<TypeLookupResult?>(CreateTypeLookupResult(mockType1)));
      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync("Type2", A<bool>._, A<string?>._, A<CancellationToken>._))
         .Returns(Task.FromResult<TypeLookupResult?>(CreateTypeLookupResult(mockType2)));

      // Act
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(
         ["Type1", "Type2"],
         cancellationToken: CT);

      // Assert
      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.True(IsSuccess(json));

      var data = GetData(json);
      Assert.Equal(2, data.GetIntOrNull("requested") ?? data.GetIntOrNull("Requested"));
      Assert.Equal(2, data.GetIntOrNull("succeeded") ?? data.GetIntOrNull("Succeeded"));
      Assert.Equal(0, data.GetIntOrNull("failed") ?? data.GetIntOrNull("Failed"));

      var results = data.GetArrayOrNull("results") ?? data.GetArrayOrNull("Results");
      Assert.NotNull(results);
      Assert.Equal(2, results.Count);
   }

   [Fact]
   public async Task GetTypeMembersBatch_ErrorDetailsAreInformative()
   {
      // Arrange
      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync(A<string>._, A<bool>._, A<string?>._, A<CancellationToken>._))
         .Returns(Task.FromResult<TypeLookupResult?>(null));

      // Act
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(
         ["MyMissingType"],
         cancellationToken: CT);

      // Assert
      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      var data = GetData(json);
      var errors = data.GetArrayOrNull("errors") ?? data.GetArrayOrNull("Errors");
      Assert.NotNull(errors);
      Assert.Single(errors);

      var error = errors[0]!;
      Assert.Equal("MyMissingType", error.GetStringOrNull("typeName") ?? error.GetStringOrNull("TypeName"));
      var errorMsg = error.GetStringOrNull("error") ?? error.GetStringOrNull("Error");
      Assert.Contains("MyMissingType", errorMsg);
   }

   #endregion
}
