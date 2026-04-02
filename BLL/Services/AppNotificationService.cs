using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class AppNotificationService : IAppNotificationService
    {
        private readonly IRepository<AppNotification> _notificationRepo;
        private readonly IRepository<GlobalUserBanRequest> _globalBanRequestRepo;
        private readonly IAppNotificationRealtimeDispatcher? _realtimeDispatcher;
        private readonly ILogger<AppNotificationService> _logger;

        public AppNotificationService(
            IRepository<AppNotification> notificationRepo,
            IRepository<GlobalUserBanRequest> globalBanRequestRepo,
            ILogger<AppNotificationService> logger,
            IAppNotificationRealtimeDispatcher? realtimeDispatcher = null)
        {
            _notificationRepo = notificationRepo;
            _globalBanRequestRepo = globalBanRequestRepo;
            _logger = logger;
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
                try
                {
                    await _realtimeDispatcher.DispatchAsync(userId, MapToPayload(notification));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Realtime notification dispatch failed for user {UserId}. Notification {NotificationId} was still saved.",
                        userId,
                        notification.Id);
                }
            }
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            var unreadNotifications = (await _notificationRepo.FindAsync(n => n.UserId == userId && !n.IsRead))
                .ToList();

            await ReconcileGlobalBanNotificationsAsync(unreadNotifications);

            return unreadNotifications.Count(notification => !notification.IsRead);
        }

        public async Task<IEnumerable<object>> GetMyNotificationsAsync(string userId)
        {
            var notifications = (await _notificationRepo.FindAsync(n => n.UserId == userId))
                .ToList();

            await ReconcileGlobalBanNotificationsAsync(notifications);

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
                    n.MetadataJson,
                    n.IsRead,
                    CreatedAt = NormalizeUtc(n.CreatedAt)
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
            var unreadNotifications = (await _notificationRepo.FindAsync(n => n.UserId == userId && !n.IsRead))
                .ToList();

            await ReconcileGlobalBanNotificationsAsync(unreadNotifications);

            return unreadNotifications
                .Where(n => !n.IsRead)
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
                MetadataJson = notification.MetadataJson,
                IsRead = notification.IsRead,
                Timestamp = NormalizeUtc(notification.CreatedAt)
            };
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private async Task ReconcileGlobalBanNotificationsAsync(IEnumerable<AppNotification> notifications)
        {
            var candidates = notifications
                .Where(IsGlobalBanNotification)
                .Select(notification => new
                {
                    Notification = notification,
                    RequestId = TryGetGlobalBanRequestId(notification)
                })
                .Where(item => item.RequestId.HasValue)
                .ToList();

            if (!candidates.Any())
            {
                return;
            }

            var requestIds = candidates
                .Select(item => item.RequestId!.Value)
                .Distinct()
                .ToList();

            var requests = (await _globalBanRequestRepo.FindAsync(request => requestIds.Contains(request.Id)))
                .ToDictionary(request => request.Id);

            var hasUpdates = false;

            foreach (var item in candidates)
            {
                if (!requests.TryGetValue(item.RequestId!.Value, out var request))
                {
                    continue;
                }

                var syncedMetadataJson = SyncGlobalBanNotificationMetadata(item.Notification.MetadataJson, request);
                var shouldMarkAsRead = !string.Equals(
                    request.Status,
                    GlobalUserBanRequestStatusConstants.Pending,
                    StringComparison.OrdinalIgnoreCase);
                var normalizedReferenceId = request.Id.ToString();

                var needsUpdate =
                    !string.Equals(item.Notification.MetadataJson, syncedMetadataJson, StringComparison.Ordinal) ||
                    !string.Equals(item.Notification.ReferenceType, "GlobalUserBanRequest", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(item.Notification.ReferenceId, normalizedReferenceId, StringComparison.Ordinal) ||
                    (shouldMarkAsRead && !item.Notification.IsRead);

                if (!needsUpdate)
                {
                    continue;
                }

                item.Notification.ReferenceType = "GlobalUserBanRequest";
                item.Notification.ReferenceId = normalizedReferenceId;
                item.Notification.MetadataJson = syncedMetadataJson;
                if (shouldMarkAsRead)
                {
                    item.Notification.IsRead = true;
                }

                _notificationRepo.Update(item.Notification);
                hasUpdates = true;
            }

            if (hasUpdates)
            {
                await _notificationRepo.SaveChangesAsync();
            }
        }

        private static bool IsGlobalBanNotification(AppNotification notification) =>
            string.Equals(notification.ReferenceType, "GlobalUserBanRequest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.ActionKey, "ResolveGlobalUserBanRequest", StringComparison.OrdinalIgnoreCase);

        private static int? TryGetGlobalBanRequestId(AppNotification notification)
        {
            if (int.TryParse(notification.ReferenceId, out var referenceId))
            {
                return referenceId;
            }

            if (string.IsNullOrWhiteSpace(notification.MetadataJson))
            {
                return null;
            }

            try
            {
                var metadata = JsonNode.Parse(notification.MetadataJson)?.AsObject();
                var requestNode = metadata?["banRequestId"];

                if (requestNode == null)
                {
                    return null;
                }

                if (requestNode is JsonValue jsonValue)
                {
                    if (jsonValue.TryGetValue<int>(out var requestId))
                    {
                        return requestId;
                    }

                    if (jsonValue.TryGetValue<string>(out var requestIdText) &&
                        int.TryParse(requestIdText, out var parsedRequestId))
                    {
                        return parsedRequestId;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string SyncGlobalBanNotificationMetadata(
            string? metadataJson,
            GlobalUserBanRequest request)
        {
            JsonObject metadata;

            try
            {
                metadata = JsonNode.Parse(metadataJson ?? "{}")?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metadata = new JsonObject();
            }

            metadata["banRequestId"] = request.Id;
            metadata["requestStatus"] = request.Status;
            metadata["decisionNote"] = request.DecisionNote;
            metadata["resolvedAt"] = request.ResolvedAt?.ToString("O");

            return metadata.ToJsonString();
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

