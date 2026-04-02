using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    /// <summary>
    /// AI-powered object detection service using GECO2 few-shot counting model.
    /// Communicates with HuggingFace Space Gradio API for inference.
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Detects objects in an image using few-shot exemplar bounding boxes.
        /// Sends the image and exemplars to the GECO2 HuggingFace Space API
        /// and returns the detection results (count + annotated image).
        /// </summary>
        /// <param name="userId">Current user making the request.</param>
        /// <param name="request">Detection request containing the assignment and exemplar boxes.</param>
        /// <returns>Detection results including object count and result image URL.</returns>
        Task<AIDetectResponse> DetectObjectsAsync(string userId, AIDetectRequest request);

        /// <summary>
        /// Checks whether the GECO2 HuggingFace Space is currently available.
        /// Useful for displaying UI status to users before they attempt detection.
        /// </summary>
        /// <returns>True if the service is reachable and ready.</returns>
        Task<bool> IsServiceAvailableAsync();
    }
}
