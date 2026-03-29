using BLL.Interfaces;
using Core.Entities;
using DAL.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class AppNotificationService : IAppNotificationService
    {
        private readonly IRepository<AppNotification> _notificationRepo;

        public AppNotificationService(IRepository<AppNotification> notificationRepo)
        {
            _notificationRepo = notificationRepo;
        }

        public async Task SendNotificationAsync(string userId, string message, string type)
        {
            var notification = new AppNotification
            {
                UserId = userId,
                Message = message,
                Type = type,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _notificationRepo.AddAsync(notification);
            await _notificationRepo.SaveChangesAsync();
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            var unreadNotifications = await _notificationRepo.FindAsync(n => n.UserId == userId && !n.IsRead);
            return unreadNotifications.Count();
        }

        public async Task<IEnumerable<object>> GetMyNotificationsAsync(string userId)
        {
            var notifications = await _notificationRepo.FindAsync(n => n.UserId == userId);

            return notifications
                .OrderByDescending(n => n.CreatedAt)
                .Take(50) 
                .Select(n => new
                {
                    n.Id,
                    n.Message,
                    n.Type,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToList();
        }

        public async Task MarkAsReadAsync(int id, string userId)
        {
            var notifications = await _notificationRepo.FindAsync(n => n.Id == id && n.UserId == userId);
            var notif = notifications.FirstOrDefault();

            if (notif == null) throw new Exception("Notification not found.");

            notif.IsRead = true;
            _notificationRepo.Update(notif);
            await _notificationRepo.SaveChangesAsync();
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            var unreadNotifs = await _notificationRepo.FindAsync(n => n.UserId == userId && !n.IsRead);

            foreach (var notif in unreadNotifs)
            {
                notif.IsRead = true;
                _notificationRepo.Update(notif);
            }

            if (unreadNotifs.Any())
            {
                await _notificationRepo.SaveChangesAsync();
            }
        }
    }
}