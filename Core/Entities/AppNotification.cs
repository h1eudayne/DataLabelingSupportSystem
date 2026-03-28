namespace Core.Entities
{
    public class AppNotification
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public User User { get; set; }

        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
