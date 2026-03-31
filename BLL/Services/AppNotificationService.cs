using BLL.Interfaces;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class AppNotificationService : IAppNotificationService
    {
        private readonly IRepository<AppNotification> _notificationRepo;
        private readonly IAppNotificationRealtimeDispatcher? _realtimeDispatcher;

        public AppNotificationService(
            IRepository<AppNotification> notificationRepo,
            IAppNotificationRealtimeDispatcher? realtimeDispatcher = null)
        {
            _notificationRepo = notificationRepo;
            _realtimeDispatcher = realtimeDispatcher;
        }

        public async Task SendNotificationAsync(
            string userId,
            string message,
            string type,
            string? referenceType = null,
            string? referenceId = null,
            string? actionKey = null,
            string? metadataJson = null)
        {
            var notification = new AppNotification
            {
                UserId = userId,
                Title = BuildTitle(type),
                Message = message,
                Type = type,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                ActionKey = actionKey,
                MetadataJson = metadataJson,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _notificationRepo.AddAsync(notification);
            await _notificationRepo.SaveChangesAsync();

            if (_realtimeDispatcher != null)
            {
                await _realtimeDispatcher.DispatchAsync(userId, MapToPayload(notification));
            }
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
                    n.Title,
                    n.Message,
                    n.Type,
                    n.ReferenceType,
                    n.ReferenceId,
                    n.ActionKey,
                    Metadata = ParseMetadata(n.MetadataJson),
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

        public async Task<IEnumerable<object>> GetUnreadNotificationsForHubAsync(string userId)
        {
            var unreadNotifications = await _notificationRepo.FindAsync(n => n.UserId == userId && !n.IsRead);

            return unreadNotifications
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(MapToPayload)
                .ToList();
        }

        private static string BuildTitle(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "Notification";
            }

            return $"{type.Trim()} Notification";
        }

        public async Task MarkListAsReadAsync(List<int> notificationIds, string userId)
        {
            var notifications = await _notificationRepo.FindAsync(n =>
                n.UserId == userId &&
                notificationIds.Contains(n.Id) &&
                !n.IsRead);

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                _notificationRepo.Update(notification);
            }

            if (notifications.Any())
            {
                await _notificationRepo.SaveChangesAsync();
            }
        }

        private static NotificationPayload MapToPayload(AppNotification notification)
        {
            return new NotificationPayload
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                Type = notification.Type,
                ReferenceType = notification.ReferenceType,
                ReferenceId = notification.ReferenceId,
                ActionKey = notification.ActionKey,
                Metadata = ParseMetadata(notification.MetadataJson),
                IsRead = notification.IsRead,
                Timestamp = notification.CreatedAt
            };
        }

        private static JsonElement? ParseMetadata(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(metadataJson);
                return document.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }
    }
}

