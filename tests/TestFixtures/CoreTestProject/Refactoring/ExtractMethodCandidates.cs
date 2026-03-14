using System.Text;

namespace CoreTestProject.Refactoring;

// ===========================================
// Code suitable for Extract Method refactoring
// ===========================================

/// <summary>
/// Class with methods containing extractable code blocks.
/// </summary>
public class ReportGenerator
{
   private readonly List<SalesRecord> _records = new();

   // Method with extractable validation block
   public string GenerateReport(DateTime startDate, DateTime endDate)
   {
      // START: This block could be extracted to ValidateDateRange
      if (startDate > endDate)
      {
         throw new ArgumentException("Start date must be before end date");
      }

      if (startDate < DateTime.MinValue.AddYears(1))
      {
         throw new ArgumentException("Start date is too early");
      }

      if (endDate > DateTime.Now)
      {
         throw new ArgumentException("End date cannot be in the future");
      }
      // END: ValidateDateRange

      var filteredRecords = _records
         .Where(r => r.Date >= startDate && r.Date <= endDate)
         .ToList();

      // START: This block could be extracted to FormatReportHeader
      var header = new StringBuilder();
      header.AppendLine("=".PadRight(50, '='));
      header.AppendLine("SALES REPORT");
      header.AppendLine($"Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
      header.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
      header.AppendLine("=".PadRight(50, '='));
      // END: FormatReportHeader

      // START: This block could be extracted to CalculateTotals
      decimal totalSales = 0;
      decimal totalTax = 0;
      int itemCount = 0;

      foreach (var record in filteredRecords)
      {
         totalSales += record.Amount;
         totalTax += record.Tax;
         itemCount += record.ItemCount;
      }

      decimal averageSale = filteredRecords.Count > 0 ? totalSales / filteredRecords.Count : 0;
      // END: CalculateTotals

      return $"{header}\nTotal: {totalSales:C}, Tax: {totalTax:C}, Items: {itemCount}, Avg: {averageSale:C}";
   }

   // Method with extractable formatting logic
   public string FormatCustomerInfo(string firstName, string lastName, string email, string phone)
   {
      // This entire block could be extracted
      var formatted = new StringBuilder();
      formatted.Append(firstName.Trim());
      formatted.Append(" ");
      formatted.Append(lastName.Trim());
      formatted.AppendLine();

      if (!string.IsNullOrEmpty(email))
      {
         formatted.Append("Email: ");
         formatted.AppendLine(email.ToLower().Trim());
      }

      if (!string.IsNullOrEmpty(phone))
      {
         // Extractable: phone formatting
         var digits = new string(phone.Where(char.IsDigit).ToArray());
         if (digits.Length == 10)
         {
            formatted.Append("Phone: ");
            formatted.AppendLine($"({digits[..3]}) {digits[3..6]}-{digits[6..]}");
         }
         else
         {
            formatted.Append("Phone: ");
            formatted.AppendLine(phone);
         }
      }

      return formatted.ToString();
   }
}

public class SalesRecord
{
   public DateTime Date { get; set; }
   public decimal Amount { get; set; }
   public decimal Tax { get; set; }
   public int ItemCount { get; set; }
}

// ===========================================
// Code suitable for Extract Variable refactoring
// ===========================================

/// <summary>
/// Class with expressions suitable for extracting to variables.
/// </summary>
public class PriceCalculator
{
   private const decimal TaxRate = 0.0825m;
   private const decimal MemberDiscount = 0.10m;
   private const decimal BulkDiscount = 0.15m;
   private const int BulkThreshold = 100;

   // Complex expressions that could be extracted to variables
   public decimal CalculateFinalPrice(decimal basePrice, int quantity, bool isMember)
   {
      // The subexpressions here could be extracted:
      // - basePrice * quantity could be "subtotal"
      // - isMember ? MemberDiscount : 0 could be "memberDiscountRate"
      // - quantity >= BulkThreshold ? BulkDiscount : 0 could be "bulkDiscountRate"
      return basePrice * quantity *
             (1 - (isMember ? MemberDiscount : 0)) *
             (1 - (quantity >= BulkThreshold ? BulkDiscount : 0)) *
             (1 + TaxRate);
   }

   // Another method with extractable expressions
   public bool IsEligibleForPromotion(Customer customer)
   {
      // These boolean expressions could be extracted
      return customer.TotalPurchases > 1000 &&
             customer.MemberSince < DateTime.Now.AddYears(-1) &&
             customer.OrderCount >= 5 &&
             !customer.HasOutstandingBalance;
   }

   // Method with repeated complex expressions
   public string GetCustomerTier(Customer customer)
   {
      // customer.TotalPurchases / customer.OrderCount appears multiple times
      if (customer.OrderCount == 0) return "New";

      if (customer.TotalPurchases / customer.OrderCount > 500) return "Platinum";
      if (customer.TotalPurchases / customer.OrderCount > 200) return "Gold";
      if (customer.TotalPurchases / customer.OrderCount > 100) return "Silver";

      return "Bronze";
   }
}

public class Customer
{
   public decimal TotalPurchases { get; set; }
   public DateTime MemberSince { get; set; }
   public int OrderCount { get; set; }
   public bool HasOutstandingBalance { get; set; }
}
