using API.Hubs;
using BLL.Interfaces;
using Core.Entities;
using DAL;           
using Microsoft.AspNetCore.SignalR;
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

            // 2. GỌI ĐÒ (Bắn SignalR cho mấy thanh niên đang Online)
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