using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace UnusedCodeTestProject;

public class CalculatorTests
{
   public CalculatorTests()
   {
   }

   [Fact]
   public void Adds()
   {
   }

   [Theory]
   [InlineData(1)]
   public void Accepts(int value)
   {
   }

   public void NotATest()
   {
   }
}

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
   [HttpGet]
   public IActionResult List() => Ok();

   public IActionResult Details(int id) => Ok(id);

   [NonAction]
   public void Audit()
   {
   }

   private void Unreachable()
   {
   }
}

public class IndexModel : PageModel
{
   public void OnGet()
   {
   }

   public void Describe()
   {
   }
}

public class Payment
{
   [JsonConstructor]
   public Payment(int amount)
   {
      Amount = amount;
   }

   public int Amount { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ScheduledAttribute : Attribute;

public static class Jobs
{
   [Scheduled]
   public static void Nightly()
   {
   }
}

public class DataDrivenTests
{
   public static IEnumerable<object[]> Cases => [[1]];

   public static IEnumerable<object[]> Unnamed => [[3]];

   [Theory]
   [MemberData("Cases")]
   [MemberData("MoreCases")]
   public void Checks(int value)
   {
   }

   [Theory]
   [ClassData(typeof(SampleData))]
   public void Reads(int value)
   {
   }

   private static TheoryData<int> MoreCases() => [2];
}

internal sealed class SampleData : TheoryData<int>
{
   public SampleData()
   {
      Add(Seed());
   }

   private static int Seed() => 4;
}

public class LibraryDataTests
{
   [Theory]
   [MemberData("Rows", MemberType = typeof(UnusedCodeTestedLibrary.LibraryData))]
   public void UsesLibraryRows(int value)
   {
   }
}
