using BLL.Services;
using Core.DTOs.Requests;
using Core.Entities;
using Core.Exceptions;
using Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BLL.Tests
{
    public class AIServiceTests
    {
        private const string SpaceBaseUrl = "https://jerpelhan-geco2-demo.hf.space";
        private const string UploadUrl = $"{SpaceBaseUrl}/gradio_api/upload";
        private const string PredictUrl = $"{SpaceBaseUrl}/gradio_api/call/initial_process";
        private const string LegacyUploadUrl = $"{SpaceBaseUrl}/upload";
        private const string LegacyPredictUrl = $"{SpaceBaseUrl}/call/initial_process";

        private readonly Mock<IAssignmentRepository> _assignmentRepositoryMock;
        private readonly StubHttpMessageHandler _httpHandler;
        private readonly AIService _service;

        public AIServiceTests()
        {
            _assignmentRepositoryMock = new Mock<IAssignmentRepository>();
            _httpHandler = new StubHttpMessageHandler();
            _service = new AIService(
                new HttpClient(_httpHandler),
                _assignmentRepositoryMock.Object,
                NullLogger<AIService>.Instance,
                (_, _) => Task.CompletedTask);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenUserHasNoAccess_ThrowsUnauthorizedAccessException()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(12))
                .ReturnsAsync(new Assignment
                {
                    Id = 12,
                    AnnotatorId = "annotator-1",
                    ReviewerId = "reviewer-1",
                    Project = new Project { Id = 3, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 7, StorageUrl = "https://cdn.example.com/image.png" }
                });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _service.DetectObjectsAsync("outsider-1", new AIDetectRequest
                {
                    AssignmentId = 12,
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 1, Ymin = 2, Xmax = 10, Ymax = 12 }
                    }
                }));
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenRepresentativeAssignmentIsNotAccessible_ResolvesAccessibleSiblingAssignment()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(3))
                .ReturnsAsync(new Assignment
                {
                    Id = 3,
                    ProjectId = 9,
                    DataItemId = 77,
                    AnnotatorId = "annotator-other",
                    ReviewerId = "reviewer-other",
                    Project = new Project { Id = 9, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 77, StorageUrl = "https://cdn.example.com/shared-image.png" },
                    ReviewLogs = new List<ReviewLog>()
                });

            _assignmentRepositoryMock
                .Setup(r => r.GetAccessibleAssignmentForUserOnDataItemAsync(9, 77, "annotator-1"))
                .ReturnsAsync(new Assignment
                {
                    Id = 33,
                    ProjectId = 9,
                    DataItemId = 77,
                    AnnotatorId = "annotator-1",
                    ReviewerId = "reviewer-2",
                    Project = new Project { Id = 9, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 77, StorageUrl = "https://cdn.example.com/shared-image.png" },
                    ReviewLogs = new List<ReviewLog>()
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/shared-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/shared-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    return JsonResponse("{\"event_id\":\"evt-sibling\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-sibling")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: [{\"path\":\"/tmp/result-sibling.png\"},1]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 3,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 10, Ymin = 20, Xmax = 50, Ymax = 80 }
                }
            });

            Assert.Equal(1, response.Count);
            Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/tmp/result-sibling.png", response.ResultImageUrl);
            _assignmentRepositoryMock.Verify(
                r => r.GetAccessibleAssignmentForUserOnDataItemAsync(9, 77, "annotator-1"),
                Times.Once);
        }

        [Fact]
        public async Task DetectObjectsAsync_UsesCanonicalAssignmentImage_AndSendsPointsPayload()
        {
            var assignment = new Assignment
            {
                Id = 31,
                AnnotatorId = "annotator-1",
                Project = new Project { Id = 8, ManagerId = "manager-1" },
                DataItem = new DataItem { Id = 101, StorageUrl = "https://cdn.example.com/item.png" }
            };

            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(31))
                .ReturnsAsync(assignment);

            string? capturedPredictBody = null;

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/item.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/input.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    capturedPredictBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return JsonResponse("{\"event_id\":\"evt-31\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-31")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: [{\"path\":\"/tmp/result.png\"},5]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 31,
                ImageUrl = "https://cdn.example.com/item.png",
                Threshold = 0.4f,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 10, Ymin = 20, Xmax = 50, Ymax = 80 }
                }
            });

            Assert.Equal(5, response.Count);
            Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/tmp/result.png", response.ResultImageUrl);
            Assert.NotNull(response.Diagnostics);
            Assert.True(response.Diagnostics!.ProviderRequestSubmitted);
            Assert.True(response.Diagnostics.ProviderResultReceived);
            Assert.True(response.Diagnostics.CompleteEventReceived);
            Assert.True(response.Diagnostics.PreviewImageReturned);
            Assert.False(response.Diagnostics.RawDetectionStateReturned);
            Assert.False(response.Diagnostics.RawDetectionsReturned);
            Assert.Equal("/gradio_api/call/initial_process", response.Diagnostics.PredictEndpoint);
            Assert.Equal("preview_image_only", response.Diagnostics.ResultSource);
            Assert.NotNull(capturedPredictBody);

            using var payload = JsonDocument.Parse(capturedPredictBody!);
            var data = payload.RootElement.GetProperty("data");
            var prompt = data[0];
            Assert.True(prompt.TryGetProperty("points", out var points));
            Assert.False(prompt.TryGetProperty("boxes", out _));
            Assert.Equal(10, points[0][0].GetInt32());
            Assert.Equal(20, points[0][1].GetInt32());
            Assert.Equal(2, points[0][2].GetInt32());
            Assert.Equal(50, points[0][3].GetInt32());
            Assert.Equal(80, points[0][4].GetInt32());
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenRequestImageDoesNotMatchAssignment_ThrowsUnauthorizedAccessException()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(77))
                .ReturnsAsync(new Assignment
                {
                    Id = 77,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 201, StorageUrl = "https://cdn.example.com/real-image.png" }
                });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
                {
                    AssignmentId = 77,
                    ImageUrl = "https://evil.example.com/not-your-image.png",
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 5, Ymin = 5, Xmax = 25, Ymax = 30 }
                    }
                }));
        }

        [Fact]
        public async Task DetectObjectsAsync_WithLocalStoredImage_LoadsFileFromWwwroot()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var uploadsFolder = Path.Combine(currentDirectory, "wwwroot", "uploads", "99");
            Directory.CreateDirectory(uploadsFolder);

            var imagePath = Path.Combine(uploadsFolder, "local-ai-test.png");
            await File.WriteAllBytesAsync(imagePath, new byte[] { 9, 8, 7, 6 });

            try
            {
                _assignmentRepositoryMock
                    .Setup(r => r.GetAssignmentWithDetailsAsync(99))
                    .ReturnsAsync(new Assignment
                    {
                        Id = 99,
                        AnnotatorId = "annotator-1",
                        Project = new Project { Id = 8, ManagerId = "manager-1" },
                        DataItem = new DataItem { Id = 301, StorageUrl = "/uploads/99/local-ai-test.png" }
                    });

                _httpHandler.Responder = request =>
                {
                    var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                    if (request.Method == HttpMethod.Post && url == UploadUrl)
                    {
                        return JsonResponse("[\"/tmp/gradio/local.png\"]");
                    }

                    if (request.Method == HttpMethod.Post && url == PredictUrl)
                    {
                        return JsonResponse("{\"event_id\":\"evt-local\"}");
                    }

                    if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-local")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                "event: complete\ndata: [{\"url\":\"https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/result-local.png\"},2]\n\n",
                                Encoding.UTF8,
                                "text/event-stream")
                        };
                    }

                    throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
                };

                var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
                {
                    AssignmentId = 99,
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 3, Ymin = 4, Xmax = 13, Ymax = 18 }
                    }
                });

                Assert.Equal(2, response.Count);
                Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/result-local.png", response.ResultImageUrl);
            }
            finally
            {
                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }

                if (Directory.Exists(uploadsFolder))
                {
                    Directory.Delete(uploadsFolder, recursive: true);
                }
            }
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenSpaceColdStarts_RetriesUntilSuccess()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(120))
                .ReturnsAsync(new Assignment
                {
                    Id = 120,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 401, StorageUrl = "https://cdn.example.com/retry-image.png" }
                });

            int predictionAttempts = 0;

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/retry-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 4, 3, 2, 1 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/retry-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    predictionAttempts++;
                    if (predictionAttempts < 3)
                    {
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent("Space is loading", Encoding.UTF8, "text/plain")
                        };
                    }

                    return JsonResponse("{\"event_id\":\"evt-retry\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-retry")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: [{\"path\":\"/tmp/retry-result.png\"},7]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 120,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 12, Ymin = 15, Xmax = 36, Ymax = 45 }
                }
            });

            Assert.Equal(7, response.Count);
            Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/tmp/retry-result.png", response.ResultImageUrl);
            Assert.Equal(3, predictionAttempts);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenSpaceStaysUnavailable_ThrowsAIServiceUnavailableException()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(121))
                .ReturnsAsync(new Assignment
                {
                    Id = 121,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 402, StorageUrl = "https://cdn.example.com/unavailable-image.png" }
                });

            int predictionAttempts = 0;

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/unavailable-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 6, 6, 6, 6 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/unavailable-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    predictionAttempts++;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("Space is still loading", Encoding.UTF8, "text/plain")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var exception = await Assert.ThrowsAsync<AIServiceUnavailableException>(() =>
                _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
                {
                    AssignmentId = 121,
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 2, Ymin = 3, Xmax = 18, Ymax = 22 }
                    }
                }));

            Assert.Contains("cold start", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(6, predictionAttempts);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenResultPayloadIsMalformed_ThrowsInvalidOperationException()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(122))
                .ReturnsAsync(new Assignment
                {
                    Id = 122,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 403, StorageUrl = "https://cdn.example.com/malformed-image.png" }
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/malformed-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 8, 8, 8, 8 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/malformed-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    return JsonResponse("{\"event_id\":\"evt-malformed\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-malformed")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: {\"unexpected\":true}\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
                {
                    AssignmentId = 122,
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 1, Ymin = 1, Xmax = 20, Ymax = 20 }
                    }
                }));

            Assert.Contains("completed detection result", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenCompleteEventUsesMultipleDataLines_ParsesResponse()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(123))
                .ReturnsAsync(new Assignment
                {
                    Id = 123,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 404, StorageUrl = "https://cdn.example.com/multiline-image.png" }
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/multiline-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 2, 4, 6, 8 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/multiline-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    return JsonResponse("{\"event_id\":\"evt-multiline\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-multiline")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: generating\ndata: null\n\n" +
                            "event: complete\n" +
                            "data: [{\"path\":\"/tmp/multiline-result.png\"},\n" +
                            "data: {\"value\":9},\n" +
                            "data: null]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 123,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 4, Ymin = 6, Xmax = 24, Ymax = 28 }
                }
            });

            Assert.Equal(9, response.Count);
            Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/tmp/multiline-result.png", response.ResultImageUrl);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenImageFetchFailsWithSslError_ReturnsHelpfulMessage()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(124))
                .ReturnsAsync(new Assignment
                {
                    Id = 124,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 405, StorageUrl = "https://cdn.example.com/ssl-image.png" }
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/ssl-image.png")
                {
                    throw new HttpRequestException(
                        "The SSL connection could not be established, see inner exception.",
                        new InvalidOperationException("The remote certificate is invalid."));
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
                {
                    AssignmentId = 124,
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 5, Ymin = 5, Xmax = 25, Ymax = 30 }
                    }
                }));

            Assert.Contains("secure SSL connection", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AllowUnsafeSslForDevelopment", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenStoredImageReturns404_ThrowsHelpfulMessage()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(126))
                .ReturnsAsync(new Assignment
                {
                    Id = 126,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 407, StorageUrl = "https://cdn.example.com/missing-image.png" }
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/missing-image.png")
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
                {
                    AssignmentId = 126,
                    Exemplars =
                    {
                        new ExemplarBox { Xmin = 5, Ymin = 5, Xmax = 25, Ymax = 30 }
                    }
                }));

            Assert.Contains("stored assignment image", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("returned 404", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenModernGradioRoutesReturn404_FallsBackToLegacyRoutes()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(125))
                .ReturnsAsync(new Assignment
                {
                    Id = 125,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 406, StorageUrl = "https://cdn.example.com/legacy-route-image.png" }
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/legacy-route-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 1, 1, 2, 3 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Post && url == LegacyUploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/legacy-input.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Post && url == LegacyPredictUrl)
                {
                    return JsonResponse("{\"event_id\":\"evt-legacy\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-legacy")
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Get && url == $"{LegacyPredictUrl}/evt-legacy")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: [{\"path\":\"/tmp/legacy-result.png\"},3]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 125,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 10, Ymin = 10, Xmax = 30, Ymax = 35 }
                }
            });

            Assert.Equal(3, response.Count);
            Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/tmp/legacy-result.png", response.ResultImageUrl);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenFirstAttemptReturnsZero_RetriesLowerThresholdsAndReturnsFallbackResult()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(127))
                .ReturnsAsync(new Assignment
                {
                    Id = 127,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 408, StorageUrl = "https://cdn.example.com/fallback-threshold-image.png" }
                });

            var submittedThresholds = new List<float>();
            int predictionAttempt = 0;

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/fallback-threshold-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 7, 7, 7, 7 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/fallback-threshold-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    submittedThresholds.Add(payload.RootElement.GetProperty("data")[2].GetSingle());
                    predictionAttempt += 1;
                    return JsonResponse($"{{\"event_id\":\"evt-fallback-{predictionAttempt}\"}}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-fallback-1")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: [{\"path\":\"/tmp/fallback-result-1.png\"},0]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-fallback-2")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: complete\ndata: [{\"path\":\"/tmp/fallback-result-2.png\"},4]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 127,
                Threshold = 0.33f,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 10, Ymin = 10, Xmax = 30, Ymax = 35 }
                }
            });

            Assert.Equal(4, response.Count);
            Assert.Equal(0.25f, response.ThresholdUsed);
            Assert.Equal(new[] { 0.33f, 0.25f }, response.ThresholdAttempts);
            Assert.Equal(new[] { 0.33f, 0.25f }, submittedThresholds);
            Assert.Contains("retrying lower thresholds", response.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenAllThresholdAttemptsReturnZero_ReturnsFinalAttemptMetadata()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(128))
                .ReturnsAsync(new Assignment
                {
                    Id = 128,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 409, StorageUrl = "https://cdn.example.com/all-zero-threshold-image.png" }
                });

            var submittedThresholds = new List<float>();
            int predictionAttempt = 0;

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/all-zero-threshold-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 5, 5, 5, 5 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/all-zero-threshold-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    submittedThresholds.Add(payload.RootElement.GetProperty("data")[2].GetSingle());
                    predictionAttempt += 1;
                    return JsonResponse($"{{\"event_id\":\"evt-all-zero-{predictionAttempt}\"}}");
                }

                if (request.Method == HttpMethod.Get && url.StartsWith($"{PredictUrl}/evt-all-zero-", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $"event: complete\ndata: [{{\"path\":\"/tmp/all-zero-{predictionAttempt}.png\"}},0]\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 128,
                Threshold = 0.33f,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 12, Ymin = 12, Xmax = 24, Ymax = 30 }
                }
            });

            Assert.Equal(0, response.Count);
            Assert.Equal(0.05f, response.ThresholdUsed);
            Assert.Equal(new[] { 0.33f, 0.25f, 0.18f, 0.12f, 0.08f, 0.05f }, response.ThresholdAttempts);
            Assert.Equal(response.ThresholdAttempts, submittedThresholds);
            Assert.Contains("down to 0.05", response.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectObjectsAsync_WhenGradioIncludesHiddenStateOutputs_ParsesRawDetections()
        {
            _assignmentRepositoryMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(129))
                .ReturnsAsync(new Assignment
                {
                    Id = 129,
                    AnnotatorId = "annotator-1",
                    Project = new Project { Id = 8, ManagerId = "manager-1" },
                    DataItem = new DataItem { Id = 410, StorageUrl = "https://cdn.example.com/raw-detections-image.png" }
                });

            _httpHandler.Responder = request =>
            {
                var url = request.RequestUri?.AbsoluteUri ?? string.Empty;

                if (request.Method == HttpMethod.Get && url == "https://cdn.example.com/raw-detections-image.png")
                {
                    var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 4, 4, 4, 4 })
                    };
                    imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return imageResponse;
                }

                if (request.Method == HttpMethod.Post && url == UploadUrl)
                {
                    return JsonResponse("[\"/tmp/gradio/raw-detections-image.png\"]");
                }

                if (request.Method == HttpMethod.Post && url == PredictUrl)
                {
                    return JsonResponse("{\"event_id\":\"evt-raw\"}");
                }

                if (request.Method == HttpMethod.Get && url == $"{PredictUrl}/evt-raw")
                {
                    var hiddenStatePayload = JsonSerializer.Serialize(new object[]
                    {
                        new { path = "/tmp/raw-result.png" },
                        0,
                        BuildFakeImageState(height: 20, width: 20),
                        new object[]
                        {
                            new
                            {
                                pred_boxes = new[]
                                {
                                    new[]
                                    {
                                        new[] { 0.10f, 0.20f, 0.50f, 0.70f },
                                        new[] { 0.11f, 0.21f, 0.51f, 0.71f },
                                        new[] { 0.60f, 0.10f, 0.90f, 0.40f }
                                    }
                                },
                                box_v = new[]
                                {
                                    new[] { 0.95f, 0.90f, 0.88f }
                                }
                            }
                        },
                        null!,
                        null!,
                        51.2f,
                        Array.Empty<int[]>()
                    });

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $"event: complete\ndata: {hiddenStatePayload}\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                }

                throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {url}");
            };

            var response = await _service.DetectObjectsAsync("annotator-1", new AIDetectRequest
            {
                AssignmentId = 129,
                Threshold = 0.33f,
                Exemplars =
                {
                    new ExemplarBox { Xmin = 2, Ymin = 2, Xmax = 8, Ymax = 10 }
                }
            });

            Assert.Equal(2, response.Count);
            Assert.Equal("https://jerpelhan-geco2-demo.hf.space/gradio_api/file=/tmp/raw-result.png", response.ResultImageUrl);
            Assert.Equal(2, response.Detections.Count);
            Assert.NotNull(response.Diagnostics);
            Assert.True(response.Diagnostics!.ProviderRequestSubmitted);
            Assert.True(response.Diagnostics.ProviderResultReceived);
            Assert.True(response.Diagnostics.CompleteEventReceived);
            Assert.True(response.Diagnostics.PreviewImageReturned);
            Assert.True(response.Diagnostics.RawDetectionStateReturned);
            Assert.True(response.Diagnostics.RawDetectionsReturned);
            Assert.Equal("raw_detections", response.Diagnostics.ResultSource);
            Assert.Equal((2, 4, 10, 14), (
                response.Detections[0].Xmin,
                response.Detections[0].Ymin,
                response.Detections[0].Xmax,
                response.Detections[0].Ymax));
            Assert.Equal((12, 2, 18, 8), (
                response.Detections[1].Xmin,
                response.Detections[1].Ymin,
                response.Detections[1].Xmax,
                response.Detections[1].Ymax));
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static int[][][] BuildFakeImageState(int height, int width)
        {
            var rows = new int[height][][];
            for (var y = 0; y < height; y++)
            {
                rows[y] = new int[width][];
                for (var x = 0; x < width; x++)
                {
                    rows[y][x] = new[] { 0 };
                }
            }

            return rows;
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (Responder == null)
                {
                    throw new InvalidOperationException("No responder configured for the HTTP handler.");
                }

                return Task.FromResult(Responder(request));
            }
        }
    }
}
