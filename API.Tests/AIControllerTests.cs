using API.Controllers;
using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace API.Tests.Controllers
{
    public class AIControllerTests
    {
        private readonly Mock<IAIService> _aiServiceMock = new();

        private AIController CreateController()
        {
            var controller = new AIController(_aiServiceMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(
                            new ClaimsIdentity(
                                new[]
                                {
                                    new Claim(ClaimTypes.NameIdentifier, "user-1")
                                },
                                "TestAuth"))
                    }
                }
            };

            return controller;
        }

        [Fact]
        public async Task DetectObjects_WhenAIServiceIsUnavailable_Returns503()
        {
            _aiServiceMock
                .Setup(s => s.DetectObjectsAsync("user-1", It.IsAny<AIDetectRequest>()))
                .ThrowsAsync(new AIServiceUnavailableException("AI service is starting up (cold start). Please wait 30-60 seconds and try again."));

            var controller = CreateController();
            var result = await controller.DetectObjects(new AIDetectRequest
            {
                AssignmentId = 5,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 1, Ymin = 1, Xmax = 10, Ymax = 10 }
                }
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        }

        [Fact]
        public async Task DetectObjects_WhenProcessingFails_Returns500()
        {
            _aiServiceMock
                .Setup(s => s.DetectObjectsAsync("user-1", It.IsAny<AIDetectRequest>()))
                .ThrowsAsync(new InvalidOperationException("GECO2 API did not return a completed detection result."));

            var controller = CreateController();
            var result = await controller.DetectObjects(new AIDetectRequest
            {
                AssignmentId = 6,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 2, Ymin = 2, Xmax = 12, Ymax = 12 }
                }
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        }

        [Fact]
        public async Task DetectObjects_WhenDetectionSucceeds_Returns200()
        {
            _aiServiceMock
                .Setup(s => s.DetectObjectsAsync("user-1", It.IsAny<AIDetectRequest>()))
                .ReturnsAsync(new AIDetectResponse
                {
                    Count = 4,
                    ResultImageUrl = "https://example.com/result.png",
                    Message = "Detected 4 object(s)."
                });

            var controller = CreateController();
            var result = await controller.DetectObjects(new AIDetectRequest
            {
                AssignmentId = 7,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 3, Ymin = 3, Xmax = 15, Ymax = 15 }
                }
            });

            Assert.IsType<OkObjectResult>(result);
        }
    }
}
