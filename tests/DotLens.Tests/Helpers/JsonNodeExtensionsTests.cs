using System.Text.Json.Nodes;
using Xunit;

namespace DotLens.Tests.Helpers;

public class JsonNodeExtensionsTests
{
   private readonly JsonNode _testNode = JsonNode.Parse("""
      {
         "name": "test",
         "count": 42,
         "active": true,
         "items": [1, 2, 3],
         "nested": { "value": "inner" },
         "nullValue": null
      }
      """)!;

   // === Required accessors ===

   [Fact]
   public void GetString_ReturnsValue_WhenPropertyExists()
   {
      Assert.Equal("test", _testNode.GetString("name"));
   }

   [Fact]
   public void GetString_Throws_WhenPropertyMissing()
   {
      var ex = Assert.Throws<InvalidOperationException>(() => _testNode.GetString("missing"));
      Assert.Contains("missing", ex.Message);
   }

   [Fact]
   public void GetString_Throws_WhenPropertyNull()
   {
      var ex = Assert.Throws<InvalidOperationException>(() => _testNode.GetString("nullValue"));
      Assert.Contains("nullValue", ex.Message);
   }

   [Fact]
   public void GetInt_ReturnsValue_WhenPropertyExists()
   {
      Assert.Equal(42, _testNode.GetInt("count"));
   }

   [Fact]
   public void GetInt_Throws_WhenPropertyMissing()
   {
      var ex = Assert.Throws<InvalidOperationException>(() => _testNode.GetInt("missing"));
      Assert.Contains("missing", ex.Message);
   }

   [Fact]
   public void GetBool_ReturnsValue_WhenPropertyExists()
   {
      Assert.True(_testNode.GetBool("active"));
   }

   [Fact]
   public void GetBool_Throws_WhenPropertyMissing()
   {
      var ex = Assert.Throws<InvalidOperationException>(() => _testNode.GetBool("missing"));
      Assert.Contains("missing", ex.Message);
   }

   [Fact]
   public void GetArray_ReturnsValue_WhenPropertyExists()
   {
      var array = _testNode.GetArray("items");
      Assert.Equal(3, array.Count);
   }

   [Fact]
   public void GetArray_Throws_WhenPropertyMissing()
   {
      var ex = Assert.Throws<InvalidOperationException>(() => _testNode.GetArray("missing"));
      Assert.Contains("missing", ex.Message);
   }

   [Fact]
   public void GetObject_ReturnsValue_WhenPropertyExists()
   {
      var obj = _testNode.GetObject("nested");
      Assert.Equal("inner", obj.GetString("value"));
   }

   [Fact]
   public void GetObject_Throws_WhenPropertyMissing()
   {
      var ex = Assert.Throws<InvalidOperationException>(() => _testNode.GetObject("missing"));
      Assert.Contains("missing", ex.Message);
   }

   // === Optional accessors ===

   [Fact]
   public void GetStringOrNull_ReturnsValue_WhenPropertyExists()
   {
      Assert.Equal("test", _testNode.GetStringOrNull("name"));
   }

   [Fact]
   public void GetStringOrNull_ReturnsNull_WhenPropertyMissing()
   {
      Assert.Null(_testNode.GetStringOrNull("missing"));
   }

   [Fact]
   public void GetStringOrNull_ReturnsNull_WhenPropertyNull()
   {
      Assert.Null(_testNode.GetStringOrNull("nullValue"));
   }

   [Fact]
   public void GetIntOrNull_ReturnsValue_WhenPropertyExists()
   {
      Assert.Equal(42, _testNode.GetIntOrNull("count"));
   }

   [Fact]
   public void GetIntOrNull_ReturnsNull_WhenPropertyMissing()
   {
      Assert.Null(_testNode.GetIntOrNull("missing"));
   }

   [Fact]
   public void GetBoolOrNull_ReturnsValue_WhenPropertyExists()
   {
      Assert.True(_testNode.GetBoolOrNull("active"));
   }

   [Fact]
   public void GetBoolOrNull_ReturnsNull_WhenPropertyMissing()
   {
      Assert.Null(_testNode.GetBoolOrNull("missing"));
   }

   [Fact]
   public void GetArrayOrNull_ReturnsValue_WhenPropertyExists()
   {
      var array = _testNode.GetArrayOrNull("items");
      Assert.NotNull(array);
      Assert.Equal(3, array.Count);
   }

   [Fact]
   public void GetArrayOrNull_ReturnsNull_WhenPropertyMissing()
   {
      Assert.Null(_testNode.GetArrayOrNull("missing"));
   }

   [Fact]
   public void GetObjectOrNull_ReturnsValue_WhenPropertyExists()
   {
      var obj = _testNode.GetObjectOrNull("nested");
      Assert.NotNull(obj);
   }

   [Fact]
   public void GetObjectOrNull_ReturnsNull_WhenPropertyMissing()
   {
      Assert.Null(_testNode.GetObjectOrNull("missing"));
   }
}
