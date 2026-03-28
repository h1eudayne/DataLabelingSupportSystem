using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{

    [Route("api/projects")]
    [ApiController]
    [Authorize]
    [Tags("3. Project Management")]
    public class ProjectDataController : ControllerBase
    {
        private readonly IProjectService _projectService;
        private readonly IWebHostEnvironment _env;

        public ProjectDataController(IProjectService projectService, IWebHostEnvironment env)
        {
            _projectService = projectService;
            _env = env;
        }

        /// <summary>
        /// ImportData endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("{projectId}/imports")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ImportData(int projectId, [FromBody] ImportDataRequest request)
        {
            try
            {
                await _projectService.ImportDataItemsAsync(projectId, request.StorageUrls);
                return Ok(new { Message = "Data items imported successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// UploadDirect endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <param name="files">The files.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("{projectId}/uploads/direct")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> UploadDirect(int projectId, [FromForm] List<IFormFile> files)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (files == null || !files.Any())
                return BadRequest(new ErrorResponse { Message = "Please select at least one file to upload." });

            try
            {

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
                const long maxFileSizeBytes = 10 * 1024 * 1024; 

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];

                    if (file.Length > maxFileSizeBytes)
                        return BadRequest(new ErrorResponse
                        {
                            Message = $"File '{file.FileName}' exceeds the maximum allowed size of 10MB."
                        });

                    var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
                        return BadRequest(new ErrorResponse
                        {
                            Message = $"File '{file.FileName}' has an unsupported format. Allowed formats: jpg, jpeg, png, gif, webp, bmp."
                        });
                }

                var webRootPath = _env.WebRootPath;
                if (string.IsNullOrWhiteSpace(webRootPath))
                {
                    webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                }
                var uploadedCount = await _projectService.UploadDirectDataItemsAsync(projectId, files, webRootPath);
                return Ok(new { Message = $"{uploadedCount} files uploaded successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetBuckets endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("{projectId}/buckets")]
        [Authorize(Roles = "Annotator,Manager,Admin")]
        [ProducesResponseType(typeof(object), 200)] 
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetBuckets(int projectId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var buckets = await _projectService.GetBucketsAsync(projectId, userId);
            return Ok(buckets);
        }

        /// <summary>
        /// ExportData endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("{projectId}/exports")]
        [Authorize(Roles = "Manager,Admin")]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ExportData(int projectId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var fileContent = await _projectService.ExportProjectDataAsync(projectId, userId);
                var fileName = $"project_{projectId}_export_{DateTime.UtcNow:yyyyMMdd}.json";
                return File(fileContent, "application/json", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// ExportCsv endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("{projectId}/export-csv")]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> ExportCsv(int projectId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var fileBytes = await _projectService.ExportProjectCsvAsync(projectId, userId);
                return File(fileBytes, "text/csv", $"Project_{projectId}_Export.csv");
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}