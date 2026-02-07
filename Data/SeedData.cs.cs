using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CEMS.Data
{
    public static class SeedData
    {
        public static async Task InitializeRolesAndAdmin(IServiceProvider serviceProvider)
        {
            Console.WriteLine("🎯 SEEDER STARTING...");

            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // DEBUG: Check database connection
            Console.WriteLine("🔍 Checking database...");

            // 1. Create roles
            Console.WriteLine("📝 Creating roles...");
            string[] roles = { "CEO", "Manager", "Driver", "Finance" };

            foreach (var role in roles)
            {
                Console.WriteLine($"   Creating role: {role}");
                var roleExist = await roleManager.RoleExistsAsync(role);

                if (!roleExist)
                {
                    var roleResult = await roleManager.CreateAsync(new IdentityRole(role));

                    if (roleResult.Succeeded)
                        Console.WriteLine($"   ✅ Role created: {role}");
                    else
                        Console.WriteLine($"   ❌ Failed to create role {role}: {string.Join(", ", roleResult.Errors)}");
                }
                else
                {
                    Console.WriteLine($"   ⚠️ Role already exists: {role}");
                }
            }

            // 2. Create users
            Console.WriteLine("\n👤 Creating users...");
            var users = new[]
            {
        new { Email = "ceo@expense.com", Role = "CEO" },
        new { Email = "manager@expense.com", Role = "Manager" },
        new { Email = "driver@expense.com", Role = "Driver" },
        new { Email = "finance@expense.com", Role = "Finance" }
    };

            foreach (var userInfo in users)
            {
                Console.WriteLine($"   Creating user: {userInfo.Email} ({userInfo.Role})");

                // Check if user exists
                var existingUser = await userManager.FindByEmailAsync(userInfo.Email);
                if (existingUser != null)
                {
                    Console.WriteLine($"   ⚠️ User already exists: {userInfo.Email}");

                    // Check if user has the role
                    var isInRole = await userManager.IsInRoleAsync(existingUser, userInfo.Role);
                    if (!isInRole)
                    {
                        Console.WriteLine($"   Adding role to existing user...");
                        await userManager.AddToRoleAsync(existingUser, userInfo.Role);
                    }
                    continue;
                }

                // Create new user
                var user = new IdentityUser
                {
                    UserName = userInfo.Email,
                    Email = userInfo.Email,
                    EmailConfirmed = true
                };

                // Try to create user with password - MAKE SURE THIS MATCHES YOUR REQUIREMENTS
                var createResult = await userManager.CreateAsync(user, "P@ssw0rd123");

                if (createResult.Succeeded)
                {
                    Console.WriteLine($"   ✅ User created: {userInfo.Email}");
                    Console.WriteLine($"   User ID: {user.Id}");

                    // Add role
                    var roleResult = await userManager.AddToRoleAsync(user, userInfo.Role);
                    if (roleResult.Succeeded)
                        Console.WriteLine($"   ✅ Role assigned: {userInfo.Role}");
                    else
                        Console.WriteLine($"   ❌ Failed to assign role: {string.Join(", ", roleResult.Errors)}");
                }
                else
                {
                    Console.WriteLine($"   ❌ Failed to create user {userInfo.Email}:");
                    foreach (var error in createResult.Errors)
                    {
                        Console.WriteLine($"      - {error.Code}: {error.Description}");
                    }
                }
            }

            Console.WriteLine("\n🎯 SEEDER COMPLETE!");
        }
    }
}