using CEMS.Data;
using CEMS.Models;
using CEMS.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
    }));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;          
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultUI()
.AddDefaultTokenProviders();

// ✅ Configure Google OAuth as External Login Provider
var googleClientId = builder.Configuration["Gmail:ClientId"];
var googleClientSecret = builder.Configuration["Gmail:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.Scope.Add("email");
            options.ClaimActions.MapJsonKey("urn:google:profile", "picture");
            
            // Force Google to show the account selection screen
            options.Events.OnRedirectToAuthorizationEndpoint = context =>
            {
                context.Response.Redirect(context.RedirectUri + "&prompt=select_account");
                return Task.CompletedTask;
            };
        });
}

// ✅ Configure Authorization
builder.Services.ConfigureApplicationCookie(options =>
{
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.LoginPath = "/Identity/Account/Login";
});

// ✅ Ensure SignInManager is available
builder.Services.AddScoped<SignInManager<IdentityUser>>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddScoped<FuelPriceService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IGmailService, GmailService>();
builder.Services.AddHttpClient<IGmailService, GmailService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// AWS S3 configuration – when BucketName is set, use real S3; otherwise fall back to DB storage
var awsBucket = builder.Configuration["AWS:BucketName"] ?? "";
if (!string.IsNullOrWhiteSpace(awsBucket))
{
    builder.Services.AddSingleton<IS3StorageService, S3StorageService>();
}
else
{
    builder.Services.AddSingleton<IS3StorageService, NoopS3StorageService>();
    Console.WriteLine("WARNING: AWS:BucketName is not configured. Receipts will be stored in the database. Set AWS:BucketName, AWS:AccessKey and AWS:SecretKey to enable S3 storage.");
}

// PayMongo configuration
builder.Services.Configure<PayMongoOptions>(builder.Configuration.GetSection("PayMongo"));
var paymongoKey = builder.Configuration["PayMongo:SecretKey"] ?? "";

// If a PayMongo secret is configured, register the real HTTP client. Otherwise register a noop
// implementation that surfaces a clear error when used. This prevents confusing errors like
// "Missing authorization header" when deployed without secrets.
if (!string.IsNullOrWhiteSpace(paymongoKey))
{
    builder.Services.AddHttpClient<IPayMongoService, PayMongoService>(client =>
    {
        client.BaseAddress = new Uri("https://api.paymongo.com/v1/");
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{paymongoKey}:"));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    });
}
else
{
    // Register a no-op implementation that throws a helpful error message when used.
    builder.Services.AddSingleton<IPayMongoService, NoopPayMongoService>();
    Console.WriteLine("WARNING: PayMongo:SecretKey is not configured. Payments are disabled. Set PayMongo:SecretKey in configuration or use environment variable PayMongo__SecretKey.");
}

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

        // ✅ Clean up broken accounts (e.g. inserted via SSMS without proper Identity fields)
        var brokenUsers = context.Users
            .Where(u => u.SecurityStamp == null || u.NormalizedEmail == null || u.PasswordHash == null)
            .ToList();
        if (brokenUsers.Count > 0)
        {
            Console.WriteLine($"🧹 Found {brokenUsers.Count} broken user(s) — removing so seeder can recreate them...");
            foreach (var broken in brokenUsers)
            {
                // Remove role assignments first
                var brokenRoles = await userManager.GetRolesAsync(broken);
                if (brokenRoles.Count > 0)
                    await userManager.RemoveFromRolesAsync(broken, brokenRoles);
                await userManager.DeleteAsync(broken);
                Console.WriteLine($"   🗑️ Removed broken user: {broken.Email}");
            }
        }

        // Create roles if they don't exist
        string[] roles = { "SuperAdmin", "CEO", "Manager", "Driver", "Finance" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                Console.WriteLine($"Created role: {role}");
            }
        }

       
        var users = new[]
        {
            new { Email = "superadmin@expense.com", Role = "SuperAdmin", Password = "Test@123", FullName = "Super Admin", Street = "123 Admin St.", Barangay = "Barangay Holy Spirit", City = "Quezon City", Province = "Metro Manila", ZipCode = "1127", Country = "Philippines", ContactNumber = "09170000001" },
            new { Email = "ceo@expense.com", Role = "CEO", Password = "Test@123", FullName = "Juan Dela Cruz", Street = "456 Ayala Ave.", Barangay = "Barangay Bel-Air", City = "Makati City", Province = "Metro Manila", ZipCode = "1209", Country = "Philippines", ContactNumber = "09170000002" },
            new { Email = "manager@expense.com", Role = "Manager", Password = "Test@123", FullName = "Maria Santos", Street = "789 Ortigas Center", Barangay = "Barangay Ugong", City = "Pasig City", Province = "Metro Manila", ZipCode = "1604", Country = "Philippines", ContactNumber = "09170000003" },
            new { Email = "driver@expense.com", Role = "Driver", Password = "Test@123", FullName = "Pedro Reyes", Street = "321 Bonifacio High St.", Barangay = "Barangay Fort Bonifacio", City = "Taguig City", Province = "Metro Manila", ZipCode = "1634", Country = "Philippines", ContactNumber = "09170000004" },
            new { Email = "finance@expense.com", Role = "Finance", Password = "Test@123", FullName = "Ana Garcia", Street = "654 Shaw Blvd.", Barangay = "Barangay Wack-Wack", City = "Mandaluyong City", Province = "Metro Manila", ZipCode = "1550", Country = "Philippines", ContactNumber = "09170000005" },
            new { Email = "test@expense.com", Role = "Manager", Password = "Test@123", FullName = "Test Manager", Street = "987 N. Domingo St.", Barangay = "Barangay San Perfecto", City = "San Juan City", Province = "Metro Manila", ZipCode = "1500", Country = "Philippines", ContactNumber = "09170000006" }
        };

        foreach (var userInfo in users)
        {
   
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

                var result = await userManager.CreateAsync(user, userInfo.Password);

                if (result.Succeeded)
                {
                    Console.WriteLine($"Created user: {userInfo.Email} with password: {userInfo.Password}");

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

                // Fix any missing Identity fields (e.g. from manual SSMS inserts)
                var needsUpdate = false;
                if (string.IsNullOrEmpty(existingUser.SecurityStamp))
                {
                    existingUser.SecurityStamp = Guid.NewGuid().ToString();
                    needsUpdate = true;
                }
                if (string.IsNullOrEmpty(existingUser.NormalizedEmail))
                {
                    existingUser.NormalizedEmail = userInfo.Email.ToUpperInvariant();
                    needsUpdate = true;
                }
                if (string.IsNullOrEmpty(existingUser.NormalizedUserName))
                {
                    existingUser.NormalizedUserName = userInfo.Email.ToUpperInvariant();
                    needsUpdate = true;
                }
                if (!existingUser.EmailConfirmed)
                {
                    existingUser.EmailConfirmed = true;
                    needsUpdate = true;
                }
                if (needsUpdate)
                {
                    await userManager.UpdateAsync(existingUser);
                    Console.WriteLine($"  -> Fixed missing Identity fields");
                }

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

        // Seed profile records for existing users
        foreach (var userInfo in users)
        {
            var u = await userManager.FindByEmailAsync(userInfo.Email);
            if (u == null) continue;

            switch (userInfo.Role)
            {
                case "CEO":
                    if (!context.CEOProfiles.Any(p => p.UserId == u.Id))
                    {
                        context.CEOProfiles.Add(new CEOProfile { UserId = u.Id, FullName = userInfo.FullName, Street = userInfo.Street, Barangay = userInfo.Barangay, City = userInfo.City, Province = userInfo.Province, ZipCode = userInfo.ZipCode, Country = userInfo.Country, ContactNumber = userInfo.ContactNumber, IsActive = true });
                        Console.WriteLine($"  -> Created CEO profile for {userInfo.Email}");
                    }
                    break;
                case "Manager":
                    if (!context.ManagerProfiles.Any(p => p.UserId == u.Id))
                    {
                        context.ManagerProfiles.Add(new ManagerProfile { UserId = u.Id, FullName = userInfo.FullName, Department = "General", Street = userInfo.Street, Barangay = userInfo.Barangay, City = userInfo.City, Province = userInfo.Province, ZipCode = userInfo.ZipCode, Country = userInfo.Country, ContactNumber = userInfo.ContactNumber, IsActive = true });
                        Console.WriteLine($"  -> Created Manager profile for {userInfo.Email}");
                    }
                    break;
                case "Finance":
                    if (!context.FinanceProfiles.Any(p => p.UserId == u.Id))
                    {
                        context.FinanceProfiles.Add(new FinanceProfile { UserId = u.Id, FullName = userInfo.FullName, Department = "Accounting", Street = userInfo.Street, Barangay = userInfo.Barangay, City = userInfo.City, Province = userInfo.Province, ZipCode = userInfo.ZipCode, Country = userInfo.Country, ContactNumber = userInfo.ContactNumber, IsActive = true });
                        Console.WriteLine($"  -> Created Finance profile for {userInfo.Email}");
                    }
                    break;
                case "Driver":
                    if (!context.DriverProfiles.Any(p => p.UserId == u.Id))
                    {
                        context.DriverProfiles.Add(new DriverProfile { UserId = u.Id, FullName = userInfo.FullName, Street = userInfo.Street, Barangay = userInfo.Barangay, City = userInfo.City, Province = userInfo.Province, ZipCode = userInfo.ZipCode, Country = userInfo.Country, ContactNumber = userInfo.ContactNumber, IsActive = true });
                        Console.WriteLine($"  -> Created Driver profile for {userInfo.Email}");
                    }
                    break;
            }
        }
        await context.SaveChangesAsync();
        Console.WriteLine("=== Profile Seeding Complete ===");

        // Seed default fuel prices if none exist
        if (!context.FuelPrices.Any())
        {
            context.FuelPrices.AddRange(
                new FuelPrice { Name = "Unleaded 91",     Description = "Regular Gasoline (91 RON)",  Price = 57.50m, Icon = "bi-droplet-fill",          CssClass = "gasoline",   Unit = "/L", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "Premium 95",      Description = "Premium Gasoline (95 RON)",  Price = 62.75m, Icon = "bi-droplet-fill",          CssClass = "premium",    Unit = "/L", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "High Octane 97+", Description = "High Performance (97+ RON)", Price = 72.30m, Icon = "bi-lightning-charge-fill", CssClass = "highoctane", Unit = "/L", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "Diesel",          Description = "Common for trucks & SUVs",   Price = 51.85m, Icon = "bi-truck",                 CssClass = "diesel",     Unit = "/L", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "Premium Diesel",  Description = "Enhanced diesel fuel",       Price = 56.40m, Icon = "bi-truck",                 CssClass = "diesel",     Unit = "/L", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "Kerosene",        Description = "Industrial & aviation use",  Price = 55.90m, Icon = "bi-fire",                  CssClass = "kerosene",   Unit = "/L", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "LPG (Autogas)",   Description = "Liquefied Petroleum Gas",    Price = 28.50m, Icon = "bi-cloud-fill",            CssClass = "lpg",        Unit = "/kg", UpdatedAt = DateTime.UtcNow },
                new FuelPrice { Name = "E10 Biofuel",     Description = "Ethanol-blended gasoline",   Price = 55.20m, Icon = "bi-leaf-fill",             CssClass = "bio",        Unit = "/L", UpdatedAt = DateTime.UtcNow }
            );
            await context.SaveChangesAsync();
            Console.WriteLine("=== Fuel Price Seeding Complete ===");
        }

        Console.WriteLine("Test credentials:");
        Console.WriteLine("superadmin@expense.com / Test@123");
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
app.UseSession();

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