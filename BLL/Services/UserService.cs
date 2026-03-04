using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using DAL.Interfaces;
using Core.Constants;
using Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace BLL.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;
        private readonly IActivityLogService _logService;

        public UserService(
            IUserRepository userRepository,
            IConfiguration configuration,
            IActivityLogService logService)
        {
            _userRepository = userRepository;
            _configuration = configuration;
            _logService = logService;
        }

        public async Task<User> RegisterAsync(string fullName, string email, string password, string role)
        {
            if (!UserRoles.IsValid(role))
                throw new Exception($"Invalid role.");

            if (await _userRepository.IsEmailExistsAsync(email))
                throw new Exception("Email already exists.");

            var user = new User
            {
                FullName = fullName,
                Email = email,
                Role = role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                PaymentInfo = new PaymentInfo()
            };
            user.PaymentInfo.UserId = user.Id;

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            return user;
        }
        public async Task UpdateAvatarAsync(string userId, string avatarUrl)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            user.AvatarUrl = avatarUrl;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }
        public async Task<string?> LoginAsync(string email, string password)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);
            if (user == null) return null;
            if (!user.IsActive)
            {
                throw new ArgumentException("Account is deactivated or banned.");
            }
            if (string.IsNullOrEmpty(user.PasswordHash)) return null;

            bool isValidPassword;
            try
            {
                isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return null;
            }

            if (!isValidPassword) return null;

            return GenerateJwtToken(user);
        }

        public async Task<User?> GetUserByIdAsync(string id)
        {
            return await _userRepository.GetUserWithPaymentInfoAsync(id);
        }

        public async Task<bool> IsEmailExistsAsync(string email)
        {
            return await _userRepository.IsEmailExistsAsync(email);
        }

        public async Task UpdatePaymentInfoAsync(string userId, string bankName, string bankAccount, string taxCode)
        {
            var user = await _userRepository.GetUserWithPaymentInfoAsync(userId);
            if (user == null) throw new Exception("User not found");

            if (user.PaymentInfo == null) user.PaymentInfo = new PaymentInfo { UserId = userId };

            user.PaymentInfo.BankName = bankName;
            user.PaymentInfo.BankAccountNumber = bankAccount;
            user.PaymentInfo.TaxCode = taxCode;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            var users = await _userRepository.GetAllAsync();
            return users.ToList();
        }

        public async Task UpdateUserAsync(string userId, UpdateUserRequest request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            if (!string.IsNullOrEmpty(request.FullName)) user.FullName = request.FullName;
            if (!string.IsNullOrEmpty(request.Email))
            {
                if (user.Email != request.Email && await _userRepository.IsEmailExistsAsync(request.Email))
                    throw new Exception("Email already exists.");
                user.Email = request.Email;
            }
            if (!string.IsNullOrEmpty(request.Role))
            {
                if (!UserRoles.IsValid(request.Role)) throw new Exception("Invalid role.");
                user.Role = request.Role;
            }
            if (!string.IsNullOrEmpty(request.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            }

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }
        public async Task ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            {
                throw new Exception("Old password is incorrect");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }

        public async Task UpdateProfileAsync(string userId, UpdateProfileRequest request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            if (!string.IsNullOrEmpty(request.FullName)) user.FullName = request.FullName;
            if (!string.IsNullOrEmpty(request.AvatarUrl)) user.AvatarUrl = request.AvatarUrl;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }
        public async Task DeleteUserAsync(string userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            user.IsActive = false;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }

        public async Task ToggleUserStatusAsync(string userId, bool isActive)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            user.IsActive = isActive;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }

        public async Task<ImportUserResponse> ImportUsersFromExcelAsync(IFormFile file, string adminId)
        {
            var response = new ImportUserResponse();
            var defaultPassword = BCrypt.Net.BCrypt.HashPassword("Password@123");

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed().RowsUsed().Skip(1);

            int rowNumber = 1;

            foreach (var row in rows)
            {
                rowNumber++;

                var email = row.Cell(1).GetValue<string>()?.Trim();
                var fullName = row.Cell(2).GetValue<string>()?.Trim();
                var role = row.Cell(3).GetValue<string>()?.Trim();

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(role))
                {
                    response.FailureCount++;
                    response.Errors.Add($"Row {rowNumber}: Missing Email, FullName or Role.");
                    continue;
                }

                if (!UserRoles.IsValid(role))
                {
                    response.FailureCount++;
                    response.Errors.Add($"Row {rowNumber}: Role '{role}' is invalid.");
                    continue;
                }

                if (await _userRepository.IsEmailExistsAsync(email))
                {
                    response.FailureCount++;
                    response.Errors.Add($"Row {rowNumber}: Email '{email}' already exists.");
                    continue;
                }

                var user = new User
                {
                    Email = email,
                    FullName = fullName,
                    Role = role,
                    PasswordHash = defaultPassword,
                    PaymentInfo = new PaymentInfo()
                };

                await _userRepository.AddAsync(user);
                await _userRepository.SaveChangesAsync();

                user.PaymentInfo.UserId = user.Id;
                _userRepository.Update(user);
                await _userRepository.SaveChangesAsync();

                response.SuccessCount++;
            }

            await _logService.LogActionAsync(
                adminId,
                "Import",
                "User",
                "Bulk Import",
                $"Imported {response.SuccessCount} users, failed {response.FailureCount} rows."
            );

            return response;
        }

        private string GenerateJwtToken(User user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = Encoding.ASCII.GetBytes(jwtSettings["Key"]!);

            string safeAvatarUrl = string.IsNullOrEmpty(user.AvatarUrl)
                ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(user.FullName ?? "User")}&background=random"
                : user.AvatarUrl;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("FullName", user.FullName ?? ""),
                    new Claim("AvatarUrl", safeAvatarUrl)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"]
            };
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}