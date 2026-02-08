using CEMS.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ✅ PROPER Identity Configuration
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;          // Simplify for testing
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ✅ Ensure SignInManager is available
builder.Services.AddScoped<SignInManager<IdentityUser>>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// ✅ APPLY MIGRATIONS ON STARTUP (replaces EnsureCreated)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var services = scope.ServiceProvider;

        // Apply any pending EF Core migrations. This ensures the database schema
        // is up-to-date with the model (creates tables like Expenses when missing).
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        // Get UserManager and RoleManager
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        Console.WriteLine("=== Starting User Seeding ===");

        // Create roles if they don't exist
        string[] roles = { "CEO", "Manager", "Driver", "Finance" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                Console.WriteLine($"Created role: {role}");
            }
        }

        // Create test users with different roles
        var users = new[]
        {
            new { Email = "ceo@expense.com", Role = "CEO", Password = "Test@123" },
            new { Email = "manager@expense.com", Role = "Manager", Password = "Test@123" },
            new { Email = "driver@expense.com", Role = "Driver", Password = "Test@123" },
            new { Email = "finance@expense.com", Role = "Finance", Password = "Test@123" },
            new { Email = "test@expense.com", Role = "Manager", Password = "Test@123" }
        };

        foreach (var userInfo in users)
        {
            // Check if user already exists
            var existingUser = await userManager.FindByEmailAsync(userInfo.Email);

            if (existingUser == null)
            {
                var user = new IdentityUser
                {
                    UserName = userInfo.Email,
                    Email = userInfo.Email,
                    EmailConfirmed = true,
                    LockoutEnabled = false
                };

                // Create user
                var result = await userManager.CreateAsync(user, userInfo.Password);

                if (result.Succeeded)
                {
                    Console.WriteLine($"Created user: {userInfo.Email} with password: {userInfo.Password}");

                    // Add to role
                    await userManager.AddToRoleAsync(user, userInfo.Role);
                    Console.WriteLine($"  -> Added to role: {userInfo.Role}");
                }
                else
                {
                    Console.WriteLine($"Failed to create user {userInfo.Email}:");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  - {error.Description}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"User already exists: {userInfo.Email}");

                // Reset password to known value
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(existingUser);
                var resetResult = await userManager.ResetPasswordAsync(existingUser, resetToken, userInfo.Password);

                if (resetResult.Succeeded)
                {
                    Console.WriteLine($"  -> Password reset to: {userInfo.Password}");
                }

                // Ensure user has correct role
                var currentRoles = await userManager.GetRolesAsync(existingUser);
                if (!currentRoles.Contains(userInfo.Role))
                {
                    // Remove existing roles
                    await userManager.RemoveFromRolesAsync(existingUser, currentRoles);
                    // Add correct role
                    await userManager.AddToRoleAsync(existingUser, userInfo.Role);
                    Console.WriteLine($"  -> Assigned to role: {userInfo.Role}");
                }
            }
        }

        Console.WriteLine("=== User Seeding Complete ===");
        Console.WriteLine("Test credentials:");
        Console.WriteLine("ceo@expense.com / Test@123");
        Console.WriteLine("manager@expense.com / Test@123");
        Console.WriteLine("driver@expense.com / Test@123");
        Console.WriteLine("finance@expense.com / Test@123");
        Console.WriteLine("test@expense.com / Test@123");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during seeding/migration: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Optional: Add a simple test endpoint
app.MapGet("/test-users", async (UserManager<IdentityUser> userManager) =>
{
    var users = await userManager.Users.ToListAsync();
    var result = new List<string>();

    foreach (var user in users)
    {
        var roles = await userManager.GetRolesAsync(user);
        result.Add($"{user.Email} - Roles: {string.Join(", ", roles)}");
    }

    return Results.Ok(result);
});

app.Run();