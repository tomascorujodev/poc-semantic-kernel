using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace PocSemanticKernel.Plugins;

public sealed record Order(int Id, string Customer, string Product, decimal Total, string Status, DateOnly OrderDate);

/// <summary>
/// Simulates a business system (orders database) with fake in-memory data.
/// In a real app these methods would call a database or an internal API.
/// </summary>
public sealed class OrdersPlugin
{
    private readonly List<Order> _orders =
    [
        new(40, "Ana García",   "Laptop Pro 14",     1450.00m, "Delivered", new DateOnly(2026, 9, 2)),
        new(41, "Ana García",   "USB-C Dock",         189.90m, "Shipped",   new DateOnly(2026, 9, 18)),
        new(42, "John Smith",   "4K Monitor 27\"",    399.00m, "Processing", new DateOnly(2026, 9, 22)),
        new(43, "John Smith",   "Mechanical Keyboard", 120.50m, "Pending",   new DateOnly(2026, 9, 23)),
        new(44, "Lucía Pérez",  "Noise-cancelling Headphones", 299.99m, "Shipped", new DateOnly(2026, 9, 20)),
    ];

    [KernelFunction, Description("Gets all orders placed by a customer.")]
    public IReadOnlyList<Order> GetOrdersByCustomer(
        [Description("Customer full name, e.g. 'Ana García'")] string customerName) =>
        _orders.Where(o => o.Customer.Contains(customerName, StringComparison.OrdinalIgnoreCase)).ToList();

    [KernelFunction, Description("Gets one order by its numeric id. Returns null if it does not exist.")]
    public Order? GetOrder([Description("The order id, e.g. 42")] int orderId) =>
        _orders.FirstOrDefault(o => o.Id == orderId);

    [KernelFunction, Description("Cancels an order. Only orders with status 'Pending' or 'Processing' can be cancelled.")]
    public string CancelOrder([Description("The order id to cancel")] int orderId)
    {
        var index = _orders.FindIndex(o => o.Id == orderId);
        if (index < 0) return $"Order {orderId} not found.";

        var order = _orders[index];
        if (order.Status is not ("Pending" or "Processing"))
            return $"Order {orderId} cannot be cancelled because its status is '{order.Status}'.";

        _orders[index] = order with { Status = "Cancelled" };
        return $"Order {orderId} was cancelled.";
    }
}
