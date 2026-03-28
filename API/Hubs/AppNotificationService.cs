using API.Hubs;
using BLL.Interfaces;
using Core.Entities;
using DAL;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace API.Services
{
    public class AppNotificationService : IAppNotificationService
    {
        private readonly IHubContext<AppNotificationHub> _hubContext;
        private readonly ApplicationDbContext _context;

        public AppNotificationService(
            IHubContext<AppNotificationHub> hubContext,
            ApplicationDbContext context)
        {
            _hubContext = hubContext;
            _context = context;
        }
        public async Task<int> GetUnreadCountAsync(string userId)
        {
            return await _context.AppNotifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        }

        public async Task SendNotificationAsync(string userId, string message, string type = "Info")
        {
            var notification = new AppNotification
            {
                UserId = userId,
                Title = type,
                Message = message,
                Type = type,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.AppNotifications.Add(notification);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.User(userId).SendAsync("ReceiveNotification", new
            {
                Id = notification.Id,
                Message = notification.Message,
                Type = notification.Type,
                IsRead = notification.IsRead,
                Timestamp = notification.CreatedAt
            });
        }
    }
}
