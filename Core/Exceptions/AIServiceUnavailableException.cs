namespace Core.Exceptions
{
    /// <summary>
    /// Thrown when the upstream AI provider is temporarily unavailable,
    /// such as during a HuggingFace Space cold start.
    /// </summary>
    public class AIServiceUnavailableException : Exception
    {
        public AIServiceUnavailableException(string message)
            : base(message)
        {
        }

        public AIServiceUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
