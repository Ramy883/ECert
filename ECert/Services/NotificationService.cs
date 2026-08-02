using ECert.Data;
using ECert.Models;

namespace ECert.Services;

public class NotificationService
{
    private readonly ECertDbContext _db;
    public NotificationService(ECertDbContext db) => _db = db;

    public async Task SendAsync(string recipient, string phone, string? email, string type, string message, string channel = "SMS", string? entityType = null, int? entityId = null)
    {
        var notification = new AppNotification
        {
            Recipient = recipient,
            PhoneNumber = phone,
            Email = email,
            NotificationType = type,
            Message = message,
            Channel = channel,
            Status = "Pending",
            CreatedAt = DateTime.Now,
            RelatedEntityType = entityType,
            RelatedEntityId = entityId
        };

        // In production, integrate with SMS/WhatsApp/Email gateway here
        // For now, mark as sent (simulated)
        notification.Status = "Sent";
        notification.SentAt = DateTime.Now;

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();
    }

    public async Task RetryAsync(int notificationId)
    {
        var notification = await _db.Notifications.FindAsync(notificationId);
        if (notification == null || notification.Status != "Failed") return;

        notification.RetryCount++;
        // Simulate retry
        notification.Status = "Sent";
        notification.SentAt = DateTime.Now;
        notification.ErrorMessage = null;
        await _db.SaveChangesAsync();
    }
}
