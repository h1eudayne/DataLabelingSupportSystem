namespace BLL.Exceptions
{
    public class EmailDeliveryException : Exception
    {
        public IReadOnlyCollection<string> FailedRecipients { get; }

        public EmailDeliveryException(
            string message,
            Exception? innerException = null,
            IEnumerable<string>? failedRecipients = null) : base(message, innerException)
        {
            FailedRecipients = (failedRecipients ?? Array.Empty<string>())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
