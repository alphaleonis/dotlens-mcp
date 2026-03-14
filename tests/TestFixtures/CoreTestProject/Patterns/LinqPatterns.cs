namespace CoreTestProject.Patterns;

using CoreTestProject.Types;

// ===========================================
// LINQ patterns for data flow testing
// ===========================================

/// <summary>
/// Class with various LINQ expressions for testing data flow analysis.
/// </summary>
public class DataProcessor
{
   private readonly List<Order> _orders = new();
   private readonly List<Product> _products = new();

   // Simple LINQ query
   public IEnumerable<string> GetActiveProductNames()
   {
      return _products
         .Where(p => p.IsActive)
         .Select(p => p.Name);
   }

   // LINQ with ordering and projection
   public IEnumerable<OrderSummary> GetOrderSummaries()
   {
      return _orders
         .Where(o => o.Status != Status.Cancelled)
         .OrderByDescending(o => o.Total)
         .Select(o => new OrderSummary
         {
            OrderId = o.Id,
            CustomerName = o.CustomerName,
            Total = o.Total,
            ItemCount = o.Items.Count
         });
   }

   // LINQ with grouping
   public Dictionary<string, decimal> GetSalesByCategory()
   {
      return _orders
         .SelectMany(o => o.Items)
         .GroupBy(i => i.Category)
         .ToDictionary(g => g.Key, g => g.Sum(i => i.Price * i.Quantity));
   }

   // LINQ with join
   public IEnumerable<OrderProductInfo> GetOrdersWithProducts()
   {
      return from order in _orders
             from item in order.Items
             join product in _products on item.ProductId equals product.Id
             select new OrderProductInfo
             {
                OrderId = order.Id,
                ProductName = product.Name,
                Quantity = item.Quantity
             };
   }

   // LINQ with aggregate functions
   public OrderStatistics GetStatistics()
   {
      var completedOrders = _orders.Where(o => o.Status == Status.Completed).ToList();

      return new OrderStatistics
      {
         TotalOrders = completedOrders.Count,
         TotalRevenue = completedOrders.Sum(o => o.Total),
         AverageOrderValue = completedOrders.Any() ? completedOrders.Average(o => o.Total) : 0,
         MaxOrderValue = completedOrders.Any() ? completedOrders.Max(o => o.Total) : 0,
         MinOrderValue = completedOrders.Any() ? completedOrders.Min(o => o.Total) : 0
      };
   }

   // LINQ with complex predicates
   public IEnumerable<Product> FindProducts(ProductFilter filter)
   {
      var query = _products.AsEnumerable();

      if (!string.IsNullOrEmpty(filter.NameContains))
      {
         query = query.Where(p => p.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase));
      }

      if (filter.MinPrice.HasValue)
      {
         query = query.Where(p => p.Price >= filter.MinPrice.Value);
      }

      if (filter.MaxPrice.HasValue)
      {
         query = query.Where(p => p.Price <= filter.MaxPrice.Value);
      }

      if (filter.Categories?.Any() == true)
      {
         query = query.Where(p => filter.Categories.Contains(p.Category));
      }

      return query
         .OrderBy(p => p.Category)
         .ThenBy(p => p.Name);
   }

   // Method-syntax vs query-syntax equivalent
   public IEnumerable<string> GetTopCustomers_QuerySyntax(int count)
   {
      return (from order in _orders
              group order by order.CustomerName into customerGroup
              orderby customerGroup.Sum(o => o.Total) descending
              select customerGroup.Key)
         .Take(count);
   }

   public IEnumerable<string> GetTopCustomers_MethodSyntax(int count)
   {
      return _orders
         .GroupBy(o => o.CustomerName)
         .OrderByDescending(g => g.Sum(o => o.Total))
         .Select(g => g.Key)
         .Take(count);
   }

   // LINQ with let clause
   public IEnumerable<ProductProfit> CalculateProfits()
   {
      return from product in _products
             let totalSales = _orders
                .SelectMany(o => o.Items)
                .Where(i => i.ProductId == product.Id)
                .Sum(i => i.Quantity)
             let revenue = totalSales * product.Price
             let cost = totalSales * product.Cost
             select new ProductProfit
             {
                ProductName = product.Name,
                TotalSales = totalSales,
                Revenue = revenue,
                Cost = cost,
                Profit = revenue - cost
             };
   }
}

// ===========================================
// Supporting types for LINQ examples
// ===========================================

public class Order
{
   public int Id { get; set; }
   public string CustomerName { get; set; } = "";
   public Status Status { get; set; }
   public decimal Total { get; set; }
   public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
   public int ProductId { get; set; }
   public string Category { get; set; } = "";
   public decimal Price { get; set; }
   public int Quantity { get; set; }
}

public class Product
{
   public int Id { get; set; }
   public string Name { get; set; } = "";
   public string Category { get; set; } = "";
   public decimal Price { get; set; }
   public decimal Cost { get; set; }
   public bool IsActive { get; set; }
}

public class OrderSummary
{
   public int OrderId { get; set; }
   public string CustomerName { get; set; } = "";
   public decimal Total { get; set; }
   public int ItemCount { get; set; }
}

public class OrderProductInfo
{
   public int OrderId { get; set; }
   public string ProductName { get; set; } = "";
   public int Quantity { get; set; }
}

public class OrderStatistics
{
   public int TotalOrders { get; set; }
   public decimal TotalRevenue { get; set; }
   public decimal AverageOrderValue { get; set; }
   public decimal MaxOrderValue { get; set; }
   public decimal MinOrderValue { get; set; }
}

public class ProductFilter
{
   public string? NameContains { get; set; }
   public decimal? MinPrice { get; set; }
   public decimal? MaxPrice { get; set; }
   public List<string>? Categories { get; set; }
}

public class ProductProfit
{
   public string ProductName { get; set; } = "";
   public int TotalSales { get; set; }
   public decimal Revenue { get; set; }
   public decimal Cost { get; set; }
   public decimal Profit { get; set; }
}
