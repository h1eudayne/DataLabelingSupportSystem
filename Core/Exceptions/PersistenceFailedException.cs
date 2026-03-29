namespace Core.Exceptions
{
    /// <summary>
    /// Thrown when a database save fails (e.g. constraint violation). Carries a user-safe detail message.
    /// </summary>
    public class PersistenceFailedException : Exception
    {
        public PersistenceFailedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
