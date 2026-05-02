using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Cryptography;
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
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            // DEBUG: Check database connection
            Console.WriteLine("🔍 Checking database...");

            // 1. Create roles
            Console.WriteLine("📝 Creating roles...");
            string[] roles = { "SuperAdmin", "CEO", "Manager", "Driver", "Finance" };

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
            
            // Get temporary seed password from configuration (should be set via User Secrets)
            var tempPassword = configuration["Seeder:TempPassword"] ?? GenerateSecurePassword();
            
            if (string.IsNullOrEmpty(configuration["Seeder:TempPassword"]))
            {
                Console.WriteLine($"   ⚠️ WARNING: Using auto-generated temporary password. Set 'Seeder:TempPassword' in User Secrets for consistency.");
            }

            var users = new[]
            {
        new { Email = "superadmin@expense.com", Role = "SuperAdmin" },
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

                // Try to create user with temporary password from configuration
                var createResult = await userManager.CreateAsync(user, tempPassword);

                if (createResult.Succeeded)
                {
                    Console.WriteLine($"   ✅ User created: {userInfo.Email}");
                    Console.WriteLine($"   User ID: {user.Id}");
                    Console.WriteLine($"   ℹ️  Temporary password set (user should change on first login)");

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

        /// <summary>
        /// Generates a cryptographically secure random password if one is not configured.
        /// Uses RandomNumberGenerator instead of System.Random (SCS0005 fix).
        /// </summary>
        private static string GenerateSecurePassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var password = new char[16];

            for (int i = 0; i < password.Length; i++)
            {
                // RandomNumberGenerator.GetInt32 is cryptographically secure
                password[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }

            return new string(password);
        }
    }
}