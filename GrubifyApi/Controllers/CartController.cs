using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using GrubifyApi.Models;

namespace GrubifyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController : ControllerBase
    {
        // In-memory cart storage (in production, use a persistent store such as Cosmos DB).
        // ConcurrentDictionary + per-cart lock make concurrent requests safe.
        // NOTE: the previous unbounded static RequestDataCache (10MB byte[] per request, never evicted)
        // was removed: it exhausted the managed heap and caused System.OutOfMemoryException / HTTP 500.
        private static readonly ConcurrentDictionary<string, Cart> UserCarts = new();

        private readonly ILogger<CartController> _logger;

        public CartController(ILogger<CartController> logger)
        {
            _logger = logger;
        }

        private static Cart GetOrCreateCart(string userId) =>
            UserCarts.GetOrAdd(userId, id => new Cart { UserId = id });

        [HttpGet("{userId}")]
        public ActionResult<Cart> GetCart(string userId)
        {
            return Ok(GetOrCreateCart(userId));
        }

        [HttpPost("{userId}/items")]
        public ActionResult<Cart> AddItemToCart(string userId, [FromBody] AddCartItemRequest request)
        {
            if (request == null || request.Quantity <= 0)
            {
                return BadRequest("Quantity must be greater than zero");
            }

            var cart = GetOrCreateCart(userId);
            lock (cart)
            {
                var existingItem = cart.Items.FirstOrDefault(i => i.FoodItemId == request.FoodItemId);

                if (existingItem != null)
                {
                    existingItem.Quantity += request.Quantity;
                    existingItem.SpecialInstructions = request.SpecialInstructions;
                }
                else
                {
                    var newItem = new CartItem
                    {
                        Id = cart.Items.Count == 0 ? 1 : cart.Items.Max(i => i.Id) + 1,
                        FoodItemId = request.FoodItemId,
                        FoodItem = GetFoodItemById(request.FoodItemId),
                        Quantity = request.Quantity,
                        SpecialInstructions = request.SpecialInstructions
                    };
                    cart.Items.Add(newItem);
                }

                _logger.LogInformation("Added food item {FoodItemId} (qty {Quantity}) to cart for user {UserId}; cart now has {ItemCount} items",
                    request.FoodItemId, request.Quantity, userId, cart.Items.Count);

                return Ok(cart);
            }
        }

        [HttpPut("{userId}/items/{itemId}")]
        public ActionResult<Cart> UpdateCartItem(string userId, int itemId, [FromBody] UpdateCartItemRequest request)
        {
            if (!UserCarts.TryGetValue(userId, out var cart))
            {
                return NotFound("Cart not found");
            }

            lock (cart)
            {
                var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

                if (item == null)
                {
                    return NotFound("Item not found in cart");
                }

                item.Quantity = request.Quantity;
                item.SpecialInstructions = request.SpecialInstructions;

                return Ok(cart);
            }
        }

        [HttpDelete("{userId}/items/{itemId}")]
        public ActionResult<Cart> RemoveItemFromCart(string userId, int itemId)
        {
            if (!UserCarts.TryGetValue(userId, out var cart))
            {
                return NotFound("Cart not found");
            }

            lock (cart)
            {
                var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

                if (item == null)
                {
                    return NotFound("Item not found in cart");
                }

                cart.Items.Remove(item);
                return Ok(cart);
            }
        }

        [HttpDelete("{userId}")]
        public ActionResult ClearCart(string userId)
        {
            if (UserCarts.TryGetValue(userId, out var cart))
            {
                lock (cart)
                {
                    cart.Items.Clear();
                }
            }
            return Ok();
        }

        // Static catalog: allocated once instead of on every request.
        private static readonly List<FoodItem> FoodItems = new()
        {
            new FoodItem { Id = 1, Name = "Margherita Pizza", Price = 16.99m, ImageUrl = "/images/food/1.jpg", RestaurantId = 1 },
            new FoodItem { Id = 2, Name = "Chicken Alfredo", Price = 19.99m, ImageUrl = "/images/food/2.jpg", RestaurantId = 1 },
            new FoodItem { Id = 3, Name = "Caesar Salad", Price = 12.99m, ImageUrl = "/images/food/3.jpg", RestaurantId = 1 },
            new FoodItem { Id = 4, Name = "California Roll", Price = 14.99m, ImageUrl = "/images/food/4.jpg", RestaurantId = 2 },
            new FoodItem { Id = 5, Name = "Spicy Tuna Roll", Price = 16.99m, ImageUrl = "/images/food/5.jpg", RestaurantId = 2 },
            new FoodItem { Id = 6, Name = "Chicken Teriyaki Bowl", Price = 18.99m, ImageUrl = "/images/food/6.jpg", RestaurantId = 2 },
            new FoodItem { Id = 7, Name = "Chicken Tikka Masala", Price = 17.99m, ImageUrl = "/images/food/7.jpg", RestaurantId = 3 },
            new FoodItem { Id = 8, Name = "Vegetable Biryani", Price = 15.99m, ImageUrl = "/images/food/8.jpg", RestaurantId = 3 },
            new FoodItem { Id = 9, Name = "Garlic Naan", Price = 4.99m, ImageUrl = "/images/food/9.jpg", RestaurantId = 3 },
            new FoodItem { Id = 10, Name = "Classic Cheeseburger", Price = 13.99m, ImageUrl = "/images/food/10.jpg", RestaurantId = 4 },
            new FoodItem { Id = 11, Name = "Crispy Chicken Sandwich", Price = 15.99m, ImageUrl = "/images/food/11.jpg", RestaurantId = 4 },
            new FoodItem { Id = 12, Name = "Sweet Potato Fries", Price = 6.99m, ImageUrl = "/images/food/12.jpg", RestaurantId = 4 },
            new FoodItem { Id = 13, Name = "Quinoa Buddha Bowl", Price = 14.99m, ImageUrl = "/images/food/13.jpg", RestaurantId = 5 },
            new FoodItem { Id = 14, Name = "Acai Berry Smoothie", Price = 8.99m, ImageUrl = "/images/food/14.jpg", RestaurantId = 5 },
            new FoodItem { Id = 15, Name = "Grilled Salmon Salad", Price = 18.99m, ImageUrl = "/images/food/15.jpg", RestaurantId = 5 }
        };

        // Helper method to get food item (in production, inject the FoodItems service / query the database)
        private static FoodItem GetFoodItemById(int foodItemId)
        {
            return FoodItems.FirstOrDefault(f => f.Id == foodItemId) ?? new FoodItem();
        }
    }

    public class AddCartItemRequest
    {
        public int FoodItemId { get; set; }
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }

    public class UpdateCartItemRequest
    {
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }
}
