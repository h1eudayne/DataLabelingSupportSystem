namespace Core.Constants
{
    
    
    
    public static class FlagTypeConstants
    {
        
        public const string CorruptedImage = "CorruptedImage";
        public const string NoMatchingLabel = "NoMatchingLabel";
        public const string DataQualityIssue = "DataQualityIssue";
        public const string AmbiguousContent = "AmbiguousContent";
        public const string DuplicateImage = "DuplicateImage";
        public const string OutOfScope = "OutOfScope";
        public const string IncorrectAnnotation = "IncorrectAnnotation";
        public const string MissingParts = "MissingParts";

        public static readonly List<string> DefaultFlags = new List<string>
        {
            CorruptedImage,
            NoMatchingLabel,
            DataQualityIssue,
            AmbiguousContent,
            DuplicateImage,
            OutOfScope
        };
    }
}
