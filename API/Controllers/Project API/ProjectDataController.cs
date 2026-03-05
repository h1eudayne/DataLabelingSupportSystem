using BLL.Interfaces;
using Core.DTOs.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace API.Controllers
{
    [Route("api/projects")]
    [ApiController]
    [Authorize]
    public class ProjectDataController : ControllerBase
    {
        private readonly IProjectService _projectService;
        private readonly IWebHostEnvironment _env;

        public ProjectDataController(IProjectService projectService, IWebHostEnvironment env)
        {
            _projectService = projectService;
            _env = env;
        }

        [HttpPost("{projectId}/import")]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> ImportData(int projectId, [FromBody] ImportDataRequest request)
        {
            try
            {
                await _projectService.ImportDataItemsAsync(projectId, request.StorageUrls);
                return Ok(new { Message = "Data items imported successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost("{projectId}/upload-direct")]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> UploadDirect(int projectId, [FromForm] List<IFormFile> files)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (files == null || !files.Any())
                return BadRequest(new { Message = "Please select at least one file to upload." });

            try
            {
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
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpGet("{projectId}/buckets")]
        [Authorize(Roles = "Annotator,Manager,Admin")]
        public async Task<IActionResult> GetBuckets(int projectId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var buckets = await _projectService.GetBucketsAsync(projectId, userId);
            return Ok(buckets);
        }

        [HttpGet("{projectId}/export")]
        [Authorize(Roles = "Manager,Admin")]
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
                return BadRequest(new { Message = ex.Message });
            }
        }
    }
}