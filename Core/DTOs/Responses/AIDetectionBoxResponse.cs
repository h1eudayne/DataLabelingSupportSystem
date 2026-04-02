namespace Core.DTOs.Responses
{
    /// <summary>
    /// A single object detection returned by the GECO2 AI service.
    /// Coordinates are expressed in pixels relative to the original assignment image.
    /// </summary>
    public class AIDetectionBoxResponse
    {
        public int Xmin { get; set; }

        public int Ymin { get; set; }

        public int Xmax { get; set; }

        public int Ymax { get; set; }

        public float Confidence { get; set; }
    }
}
