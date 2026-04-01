using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Exceptions;
using Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BLL.Services
{
    /// <summary>
    /// AI detection service that calls the GECO2 HuggingFace Space Gradio API.
    /// GECO2 is a few-shot object counting and detection model (AAAI 2026).
    /// This service validates assignment access inside the business layer and then
    /// proxies the canonical assignment image to the public HuggingFace Space.
    /// </summary>
    public class AIService : IAIService
    {
        private const string HF_SPACE_URL = "https://jerpelhan-geco2-demo.hf.space";
        private const string GRADIO_API_PREFIX = "/gradio_api";
        private const string FILE_ENDPOINT_PREFIX = $"{GRADIO_API_PREFIX}/file=";
        private const int BOX_PROMPT_KIND = 2;
        private const float GECO2_CANONICAL_INPUT_SIZE = 1024f;
        private static readonly float[] ThresholdFallbacks = { 0.25f, 0.18f, 0.12f, 0.08f, 0.05f };
        private static readonly string[] UploadEndpointCandidates =
        {
            $"{GRADIO_API_PREFIX}/upload",
            "/upload"
        };
        private static readonly string[] PredictEndpointCandidates =
        {
            $"{GRADIO_API_PREFIX}/call/initial_process",
            "/call/initial_process"
        };
        private static readonly TimeSpan[] ColdStartRetrySchedule =
        {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15)
        };
        private const string ColdStartMessage = "AI service is starting up (cold start). Please wait 30-60 seconds and try again.";

        private readonly HttpClient _httpClient;
        private readonly IAssignmentRepository _assignmentRepository;
        private readonly ILogger<AIService> _logger;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AIService(
            HttpClient httpClient,
            IAssignmentRepository assignmentRepository,
            ILogger<AIService> logger,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _delayAsync = delayAsync ?? DelayAsync;

            _httpClient.Timeout = TimeSpan.FromMinutes(3);
        }

        /// <inheritdoc />
        public async Task<AIDetectResponse> DetectObjectsAsync(string userId, AIDetectRequest request)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthorizedAccessException("Authentication is required.");

            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.AssignmentId <= 0)
                throw new ArgumentException("AssignmentId is required.", nameof(request));
            if (request.Exemplars == null || request.Exemplars.Count == 0)
                throw new ArgumentException("At least one exemplar bounding box is required.", nameof(request));

            var assignment = await _assignmentRepository.GetAssignmentWithDetailsAsync(request.AssignmentId);
            if (assignment == null)
                throw new KeyNotFoundException("Assignment not found.");

            assignment = await ResolveAccessibleAssignmentAsync(userId, assignment);

            var canonicalImageUrl = assignment.DataItem?.StorageUrl;
            if (string.IsNullOrWhiteSpace(canonicalImageUrl))
                throw new InvalidOperationException("Assignment image is not available.");

            if (!string.IsNullOrWhiteSpace(request.ImageUrl) &&
                !ImageUrlMatchesAssignment(request.ImageUrl, canonicalImageUrl))
            {
                throw new UnauthorizedAccessException("The provided image does not belong to the requested assignment.");
            }

            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "Starting GECO2 detection for assignment {AssignmentId}: CanonicalImage={ImageUrl}, User={UserId}, Exemplars={ExemplarCount}, Threshold={Threshold}",
                request.AssignmentId,
                canonicalImageUrl,
                userId,
                request.Exemplars.Count,
                request.Threshold);

            try
            {
                var preparedImage = await PrepareImageAsync(canonicalImageUrl);
                var imageFileData = await UploadImageAsync(preparedImage);
                var (resultImageUrl, count, detections, diagnostics, thresholdUsed, thresholdAttempts) =
                    await DetectWithThresholdFallbackAsync(imageFileData, request);

                stopwatch.Stop();

                var response = new AIDetectResponse
                {
                    Count = count,
                    ResultImageUrl = resultImageUrl,
                    Detections = detections,
                    Diagnostics = diagnostics,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    ThresholdUsed = thresholdUsed,
                    ThresholdAttempts = thresholdAttempts,
                    MasksEnabled = request.EnableMask,
                    Message = BuildDetectionMessage(
                        count,
                        stopwatch.ElapsedMilliseconds,
                        thresholdUsed,
                        thresholdAttempts)
                };

                _logger.LogInformation(
                    "GECO2 detection completed for assignment {AssignmentId}: Count={Count}, RawDetections={RawDetections}, Time={TimeMs}ms, ThresholdUsed={ThresholdUsed}, Attempts={AttemptCount}",
                    request.AssignmentId,
                    count,
                    detections.Count,
                    stopwatch.ElapsedMilliseconds,
                    thresholdUsed,
                    thresholdAttempts.Count);

                return response;
            }
            catch (TaskCanceledException ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "GECO2 detection timed out for assignment {AssignmentId} after {TimeMs}ms",
                    request.AssignmentId,
                    stopwatch.ElapsedMilliseconds);
                throw new TimeoutException(
                    "AI detection timed out. The model may be loading. Please try again in 1-2 minutes.",
                    ex);
            }
            catch (AIServiceUnavailableException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (KeyNotFoundException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "AI detection could not reach an upstream resource for assignment {AssignmentId} after {TimeMs}ms",
                    request.AssignmentId,
                    stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException(
                    BuildUpstreamConnectivityMessage(ex),
                    ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "GECO2 detection failed for assignment {AssignmentId} after {TimeMs}ms",
                    request.AssignmentId,
                    stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException(
                    $"AI detection failed: {ex.Message}",
                    ex);
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsServiceAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{HF_SPACE_URL}{GRADIO_API_PREFIX}/info");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GECO2 service availability check failed.");
                return false;
            }
        }

        #region Private Helpers

        private async Task<(string? resultImageUrl, int count, List<AIDetectionBoxResponse> detections, AIDetectionDiagnosticsResponse? diagnostics, float thresholdUsed, List<float> thresholdAttempts)>
            DetectWithThresholdFallbackAsync(string imageFileData, AIDetectRequest request)
        {
            string? lastResultImageUrl = null;
            int lastCount = 0;
            var lastDetections = new List<AIDetectionBoxResponse>();
            AIDetectionDiagnosticsResponse? lastDiagnostics = null;
            float lastThresholdUsed = request.Threshold;
            var thresholdAttempts = new List<float>();

            foreach (var threshold in BuildThresholdSequence(request.Threshold))
            {
                var normalizedThreshold = NormalizeThreshold(threshold);
                thresholdAttempts.Add(normalizedThreshold);

                _logger.LogInformation(
                    "Submitting GECO2 detection attempt {AttemptNumber} for assignment {AssignmentId} with threshold {Threshold}",
                    thresholdAttempts.Count,
                    request.AssignmentId,
                    normalizedThreshold);

                var gradioPayload = BuildGradioPayload(imageFileData, request, normalizedThreshold);
                var submission = await SubmitPredictionAsync(gradioPayload);
                var predictionResult = await GetPredictionResultAsync(
                    submission.EventId,
                    normalizedThreshold,
                    submission.Endpoint);

                lastResultImageUrl = predictionResult.ResultImageUrl;
                lastCount = predictionResult.Count;
                lastDetections = predictionResult.Detections;
                lastDiagnostics = MergeDiagnostics(
                    predictionResult.Diagnostics,
                    providerRequestSubmitted: true,
                    providerUrl: HF_SPACE_URL,
                    predictEndpoint: submission.Endpoint,
                    eventId: submission.EventId);
                lastThresholdUsed = normalizedThreshold;

                if (predictionResult.Count > 0 || predictionResult.Detections.Count > 0)
                {
                    break;
                }
            }

            return (lastResultImageUrl, lastCount, lastDetections, lastDiagnostics, lastThresholdUsed, thresholdAttempts);
        }

        private async Task<Assignment> ResolveAccessibleAssignmentAsync(string userId, Assignment assignment)
        {
            if (CanUserAccessAssignment(userId, assignment))
            {
                return assignment;
            }

            var siblingAssignment = await _assignmentRepository.GetAccessibleAssignmentForUserOnDataItemAsync(
                assignment.ProjectId,
                assignment.DataItemId,
                userId);

            if (siblingAssignment != null && CanUserAccessAssignment(userId, siblingAssignment))
            {
                _logger.LogInformation(
                    "Resolved AI detection access from assignment {RequestedAssignmentId} to sibling assignment {ResolvedAssignmentId} for user {UserId} on project {ProjectId}, data item {DataItemId}.",
                    assignment.Id,
                    siblingAssignment.Id,
                    userId,
                    assignment.ProjectId,
                    assignment.DataItemId);
                return siblingAssignment;
            }

            throw new UnauthorizedAccessException("You do not have access to this assignment.");
        }

        private static bool CanUserAccessAssignment(string userId, Assignment assignment)
        {
            bool isAnnotator = string.Equals(assignment.AnnotatorId, userId, StringComparison.OrdinalIgnoreCase);
            bool isReviewer = string.Equals(assignment.ReviewerId, userId, StringComparison.OrdinalIgnoreCase) ||
                              assignment.ReviewLogs.Any(log => string.Equals(log.ReviewerId, userId, StringComparison.OrdinalIgnoreCase));
            bool isManager = string.Equals(assignment.Project?.ManagerId, userId, StringComparison.OrdinalIgnoreCase);

            return isAnnotator || isReviewer || isManager;
        }

        private static List<float> BuildThresholdSequence(float requestedThreshold)
        {
            var normalizedThreshold = NormalizeThreshold(requestedThreshold);
            var thresholds = new List<float> { normalizedThreshold };

            foreach (var candidate in ThresholdFallbacks)
            {
                var normalizedCandidate = NormalizeThreshold(candidate);
                if (normalizedCandidate < normalizedThreshold && !thresholds.Contains(normalizedCandidate))
                {
                    thresholds.Add(normalizedCandidate);
                }
            }

            return thresholds;
        }

        private static float NormalizeThreshold(float threshold)
        {
            return MathF.Round(Math.Clamp(threshold, 0.05f, 0.95f), 2);
        }

        private static string BuildDetectionMessage(
            int count,
            long processingTimeMs,
            float thresholdUsed,
            IReadOnlyCollection<float> thresholdAttempts)
        {
            if (count > 0)
            {
                if (thresholdAttempts.Count > 1)
                {
                    return $"Detected {count} object(s) in {processingTimeMs}ms after retrying lower thresholds down to {thresholdUsed.ToString("0.00", CultureInfo.InvariantCulture)}.";
                }

                return $"Detected {count} object(s) in {processingTimeMs}ms.";
            }

            if (thresholdAttempts.Count > 1)
            {
                return $"No objects detected after retrying lower thresholds down to {thresholdUsed.ToString("0.00", CultureInfo.InvariantCulture)}. Try adjusting the threshold or providing different exemplars.";
            }

            return "No objects detected. Try adjusting the threshold or providing different exemplars.";
        }

        private async Task<PreparedImage> PrepareImageAsync(string imageSource)
        {
            if (Uri.TryCreate(imageSource, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                using var response = await _httpClient.GetAsync(imageSource);
                try
                {
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException(
                        $"The stored assignment image could not be downloaded because the upstream image URL returned 404: {imageSource}",
                        ex);
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var fileName = Path.GetFileName(absoluteUri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "image.jpg";
                }

                return new PreparedImage(
                    fileName,
                    bytes,
                    response.Content.Headers.ContentType?.MediaType ?? GuessContentType(fileName));
            }

            var localPath = ResolveLocalAssetPath(imageSource);
            if (!File.Exists(localPath))
            {
                throw new FileNotFoundException("Assignment image file was not found on the server.", localPath);
            }

            return new PreparedImage(
                Path.GetFileName(localPath),
                await File.ReadAllBytesAsync(localPath),
                GuessContentType(localPath));
        }

        /// <summary>
        /// Uploads an image to the HuggingFace Space temporary storage.
        /// Returns the file path reference needed for the Gradio API call.
        /// </summary>
        private async Task<string> UploadImageAsync(PreparedImage image)
        {
            _logger.LogDebug("Uploading image to HuggingFace Space: {FileName}", image.FileName);

            foreach (var endpoint in UploadEndpointCandidates)
            {
                using var uploadResponse = await SendSpaceRequestWithColdStartRetryAsync(
                    async () =>
                    {
                        using var content = CreateUploadContent(image);
                        return await _httpClient.PostAsync($"{HF_SPACE_URL}{endpoint}", content);
                    },
                    $"image upload via {endpoint}");

                if (uploadResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("GECO2 upload endpoint returned 404: {Endpoint}", endpoint);
                    continue;
                }

                uploadResponse.EnsureSuccessStatusCode();

                var uploadResult = await uploadResponse.Content.ReadAsStringAsync();
                var files = JsonSerializer.Deserialize<List<string>>(uploadResult, JsonOptions);
                if (files == null || files.Count == 0 || string.IsNullOrWhiteSpace(files[0]))
                {
                    throw new InvalidOperationException("Image upload to HuggingFace failed: no file path returned.");
                }

                _logger.LogDebug("Image uploaded successfully: {FilePath}", files[0]);
                return files[0];
            }

            throw new HttpRequestException(
                "AI upload endpoint was not found on the upstream provider.",
                null,
                HttpStatusCode.NotFound);
        }

        private static MultipartFormDataContent CreateUploadContent(PreparedImage image)
        {
            var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(image.Content);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            content.Add(byteContent, "files", image.FileName);
            return content;
        }

        /// <summary>
        /// Builds the Gradio API payload for the /initial_process endpoint.
        /// GECO2's demo uses ImagePrompter, which expects ip_data = { image, points }.
        /// Each exemplar box is encoded as [xmin, ymin, 2, xmax, ymax].
        /// </summary>
        private static object BuildGradioPayload(string imageFilePath, AIDetectRequest request, float threshold)
        {
            var points = request.Exemplars
                .Select(e => new[] { e.Xmin, e.Ymin, BOX_PROMPT_KIND, e.Xmax, e.Ymax })
                .ToList();

            return new
            {
                data = new object[]
                {
                    new
                    {
                        image = new
                        {
                            path = imageFilePath,
                            meta = new { _type = "gradio.FileData" }
                        },
                        points
                    },
                    request.EnableMask,
                    threshold
                }
            };
        }

        /// <summary>
        /// Submits a prediction request to the Gradio API and returns the event_id.
        /// Gradio's async API: POST /call/{fn_name} → returns { event_id: "..." }.
        /// </summary>
        private async Task<PredictionSubmissionResult> SubmitPredictionAsync(object payload)
        {
            _logger.LogDebug("Submitting prediction request to GECO2...");

            foreach (var endpoint in PredictEndpointCandidates)
            {
                using var response = await SendSpaceRequestWithColdStartRetryAsync(
                    () => _httpClient.PostAsJsonAsync($"{HF_SPACE_URL}{endpoint}", payload, JsonOptions),
                    $"prediction submission via {endpoint}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("GECO2 prediction endpoint returned 404: {Endpoint}", endpoint);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var resultJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<GradioEventResponse>(resultJson, JsonOptions);

                if (string.IsNullOrWhiteSpace(result?.EventId))
                {
                    throw new InvalidOperationException("GECO2 API did not return an event_id.");
                }

                _logger.LogDebug("Prediction submitted, event_id={EventId}", result.EventId);
                return new PredictionSubmissionResult(result.EventId, endpoint);
            }

            throw new HttpRequestException(
                "AI prediction endpoint was not found on the upstream provider.",
                null,
                HttpStatusCode.NotFound);
        }

        /// <summary>
        /// Polls the Gradio API for the prediction result using server-sent events (SSE).
        /// GET /call/{fn_name}/{event_id} → SSE stream with final result.
        /// When HuggingFace / Gradio includes hidden state outputs, this also extracts
        /// raw detection boxes directly from the upstream GECO2 response.
        /// </summary>
        private async Task<ParsedDetectionResult> GetPredictionResultAsync(
            string eventId,
            float threshold,
            string? preferredEndpoint = null)
        {
            _logger.LogDebug("Polling for prediction result, event_id={EventId}", eventId);

            foreach (var endpoint in BuildPredictEndpointSequence(preferredEndpoint))
            {
                var resultUrl = $"{HF_SPACE_URL}{endpoint}/{eventId}";
                using var response = await SendSpaceRequestWithColdStartRetryAsync(
                    () => _httpClient.GetAsync(resultUrl, HttpCompletionOption.ResponseHeadersRead),
                    $"prediction result polling via {endpoint}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("GECO2 result endpoint returned 404: {Endpoint}", endpoint);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();

                ParsedDetectionResult? parsedResult = null;

                foreach (var sseEvent in ParseSseEvents(responseContent))
                {
                    if (!string.IsNullOrWhiteSpace(sseEvent.EventName) &&
                        !string.Equals(sseEvent.EventName, "complete", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dataJson = sseEvent.Data;
                    if (string.IsNullOrWhiteSpace(dataJson) || dataJson == "null")
                    {
                        continue;
                    }

                    if (TryParseResultEvent(dataJson, threshold, out var eventResult) &&
                        eventResult is not null)
                    {
                        parsedResult = eventResult with
                        {
                            Diagnostics = MergeDiagnostics(
                                eventResult.Diagnostics,
                                providerResultReceived: true,
                                completeEventReceived: true,
                                providerUrl: HF_SPACE_URL,
                                predictEndpoint: endpoint,
                                eventId: eventId)
                        };
                    }
                }

                if (parsedResult == null)
                {
                    throw new InvalidOperationException("GECO2 API did not return a completed detection result.");
                }

                _logger.LogInformation(
                    "GECO2 result received: Count={Count}, RawDetections={RawDetections}, ImageUrl={ImageUrl}",
                    parsedResult.Count,
                    parsedResult.Detections.Count,
                    parsedResult.ResultImageUrl ?? "none");

                return parsedResult;
            }

            throw new HttpRequestException(
                "AI result endpoint was not found on the upstream provider.",
                null,
                HttpStatusCode.NotFound);
        }

        private static IEnumerable<string> BuildPredictEndpointSequence(string? preferredEndpoint)
        {
            if (!string.IsNullOrWhiteSpace(preferredEndpoint))
            {
                yield return preferredEndpoint;
            }

            foreach (var endpoint in PredictEndpointCandidates)
            {
                if (!string.Equals(endpoint, preferredEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    yield return endpoint;
                }
            }
        }

        private static IEnumerable<SseEvent> ParseSseEvents(string responseContent)
        {
            var blocks = responseContent
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var block in blocks)
            {
                string? eventName = null;
                var dataLines = new List<string>();

                foreach (var rawLine in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                    {
                        eventName = line["event:".Length..].Trim();
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        dataLines.Add(line["data:".Length..].TrimStart());
                    }
                }

                if (dataLines.Count == 0)
                {
                    continue;
                }

                yield return new SseEvent(eventName, string.Join("\n", dataLines));
            }
        }

        private async Task<HttpResponseMessage> SendSpaceRequestWithColdStartRetryAsync(
            Func<Task<HttpResponseMessage>> sendAsync,
            string operationName)
        {
            int maxAttempts = ColdStartRetrySchedule.Length + 1;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var response = await sendAsync();
                    if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                    {
                        return response;
                    }

                    response.Dispose();
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt == maxAttempts)
                    {
                        _logger.LogWarning(
                            ex,
                            "GECO2 Space remained unavailable during {OperationName} after {AttemptCount} attempts.",
                            operationName,
                            attempt);
                        throw new AIServiceUnavailableException(ColdStartMessage, ex);
                    }

                    var retryDelay = ColdStartRetrySchedule[attempt - 1];
                    _logger.LogWarning(
                        ex,
                        "GECO2 Space returned 503 during {OperationName}, attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.",
                        operationName,
                        attempt,
                        maxAttempts,
                        retryDelay.TotalSeconds);
                    await _delayAsync(retryDelay, CancellationToken.None);
                    continue;
                }

                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "GECO2 Space remained unavailable during {OperationName} after {AttemptCount} attempts.",
                        operationName,
                        attempt);
                    throw new AIServiceUnavailableException(ColdStartMessage);
                }

                var delay = ColdStartRetrySchedule[attempt - 1];
                _logger.LogWarning(
                    "GECO2 Space returned 503 during {OperationName}, attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.",
                    operationName,
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds);
                await _delayAsync(delay, CancellationToken.None);
            }

            throw new AIServiceUnavailableException(ColdStartMessage);
        }

        private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }

        private static bool TryParseResultEvent(string dataJson, float threshold, out ParsedDetectionResult? result)
        {
            result = null;

            try
            {
                using var document = JsonDocument.Parse(dataJson);
                var root = document.RootElement;

                if (TryGetDataArray(root, out var dataArray))
                {
                    return TryParseDataArray(dataArray, threshold, out result);
                }
            }
            catch (JsonException)
            {
            }

            return false;
        }

        private static bool TryGetDataArray(JsonElement root, out JsonElement dataArray)
        {
            dataArray = default;

            if (root.ValueKind == JsonValueKind.Array)
            {
                dataArray = root;
                return true;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("data", out dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            if (root.TryGetProperty("output", out var output) &&
                output.ValueKind == JsonValueKind.Object &&
                output.TryGetProperty("data", out dataArray) &&
                dataArray.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            return false;
        }

        private static bool TryParseDataArray(JsonElement dataArray, float threshold, out ParsedDetectionResult? result)
        {
            result = null;

            if (dataArray.ValueKind != JsonValueKind.Array || dataArray.GetArrayLength() < 2)
            {
                return false;
            }

            var items = dataArray.EnumerateArray().ToList();
            string? imageUrl = null;
            int? count = null;

            if (items[0].ValueKind == JsonValueKind.Object)
            {
                if (items[0].TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                {
                    imageUrl = NormalizeHuggingFaceFileUrl(urlProp.GetString());
                }
                else if (items[0].TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                {
                    imageUrl = NormalizeHuggingFaceFileUrl(pathProp.GetString());
                }
            }

            if (TryParseCountValue(items[1], out var parsedCount))
            {
                count = parsedCount;
            }

            var rawDetectionStateReturned = TryParseRawDetections(items, threshold, out var rawDetections);
            var detections = rawDetectionStateReturned
                ? rawDetections
                : new List<AIDetectionBoxResponse>();

            var normalizedCount = detections.Count > 0
                ? detections.Count
                : count ?? 0;

            if (normalizedCount == 0 && string.IsNullOrWhiteSpace(imageUrl))
            {
                return false;
            }

            result = new ParsedDetectionResult(
                imageUrl,
                normalizedCount,
                detections,
                new AIDetectionDiagnosticsResponse
                {
                    PreviewImageReturned = !string.IsNullOrWhiteSpace(imageUrl),
                    RawDetectionStateReturned = rawDetectionStateReturned,
                    RawDetectionsReturned = detections.Count > 0,
                    OutputItemsCount = items.Count,
                    ResultSource = DetermineResultSource(
                        !string.IsNullOrWhiteSpace(imageUrl),
                        rawDetectionStateReturned,
                        detections.Count)
                });
            return true;
        }

        private static string DetermineResultSource(
            bool previewImageReturned,
            bool rawDetectionStateReturned,
            int rawDetectionsCount)
        {
            if (rawDetectionsCount > 0)
            {
                return "raw_detections";
            }

            if (previewImageReturned && rawDetectionStateReturned)
            {
                return "preview_with_raw_state";
            }

            if (previewImageReturned)
            {
                return "preview_image_only";
            }

            if (rawDetectionStateReturned)
            {
                return "raw_state_only";
            }

            return "empty";
        }

        private static AIDetectionDiagnosticsResponse MergeDiagnostics(
            AIDetectionDiagnosticsResponse source,
            bool? providerRequestSubmitted = null,
            bool? providerResultReceived = null,
            bool? completeEventReceived = null,
            string? providerUrl = null,
            string? predictEndpoint = null,
            string? eventId = null)
        {
            return new AIDetectionDiagnosticsResponse
            {
                ProviderRequestSubmitted = providerRequestSubmitted ?? source.ProviderRequestSubmitted,
                ProviderResultReceived = providerResultReceived ?? source.ProviderResultReceived,
                CompleteEventReceived = completeEventReceived ?? source.CompleteEventReceived,
                PreviewImageReturned = source.PreviewImageReturned,
                RawDetectionStateReturned = source.RawDetectionStateReturned,
                RawDetectionsReturned = source.RawDetectionsReturned,
                ProviderUrl = providerUrl ?? source.ProviderUrl,
                PredictEndpoint = predictEndpoint ?? source.PredictEndpoint,
                EventId = eventId ?? source.EventId,
                OutputItemsCount = source.OutputItemsCount,
                ResultSource = source.ResultSource
            };
        }

        private static bool TryParseCountValue(JsonElement element, out int parsedCount)
        {
            parsedCount = 0;

            var normalizedElement = NormalizeStateElement(element);
            if (normalizedElement.ValueKind == JsonValueKind.Number)
            {
                parsedCount = (int)Math.Round(normalizedElement.GetDouble());
                return true;
            }

            if (normalizedElement.ValueKind == JsonValueKind.String &&
                int.TryParse(normalizedElement.GetString(), out parsedCount))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseRawDetections(
            IReadOnlyList<JsonElement> items,
            float threshold,
            out List<AIDetectionBoxResponse> detections)
        {
            detections = new List<AIDetectionBoxResponse>();

            if (items.Count < 7)
            {
                return false;
            }

            if (!TryGetPredictionOutputs(items[3], out var predBoxesElement, out var boxScoresElement))
            {
                return false;
            }

            if (!TryParseFloatValue(items[6], out var scale) || scale <= 0f)
            {
                return false;
            }

            var normalizedBoxes = new List<float[]>();
            var scores = new List<float>();

            if (!TryCollectBoxVectors(predBoxesElement, normalizedBoxes) ||
                !TryCollectScalarFloats(boxScoresElement, scores))
            {
                return false;
            }

            var candidateCount = Math.Min(normalizedBoxes.Count, scores.Count);
            if (candidateCount == 0)
            {
                return true;
            }

            int? originalWidth = null;
            int? originalHeight = null;
            if (TryGetImageDimensions(items[2], out var parsedWidth, out var parsedHeight))
            {
                originalWidth = parsedWidth;
                originalHeight = parsedHeight;
            }

            var scoreThreshold = scores.Take(candidateCount).Max() * NormalizeThreshold(threshold);
            var selectedCandidates = new List<DetectionCandidate>();

            for (var index = 0; index < candidateCount; index++)
            {
                var score = scores[index];
                if (score <= scoreThreshold)
                {
                    continue;
                }

                var normalizedBox = ClampNormalizedBox(normalizedBoxes[index]);
                if (normalizedBox == null)
                {
                    continue;
                }

                selectedCandidates.Add(new DetectionCandidate(
                    normalizedBox[0],
                    normalizedBox[1],
                    normalizedBox[2],
                    normalizedBox[3],
                    score));
            }

            if (selectedCandidates.Count == 0)
            {
                return true;
            }

            foreach (var candidate in ApplyNonMaximumSuppression(selectedCandidates, 0.5f))
            {
                var detection = ConvertCandidateToDetection(candidate, scale, originalWidth, originalHeight);
                if (detection != null)
                {
                    detections.Add(detection);
                }
            }

            return true;
        }

        private static bool TryGetPredictionOutputs(
            JsonElement outputsState,
            out JsonElement predBoxesElement,
            out JsonElement boxScoresElement)
        {
            predBoxesElement = default;
            boxScoresElement = default;

            var normalizedOutputs = NormalizeStateElement(outputsState);
            if (normalizedOutputs.ValueKind != JsonValueKind.Array || normalizedOutputs.GetArrayLength() == 0)
            {
                return false;
            }

            var firstOutput = NormalizeStateElement(normalizedOutputs[0]);
            if (firstOutput.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!firstOutput.TryGetProperty("pred_boxes", out predBoxesElement) ||
                !firstOutput.TryGetProperty("box_v", out boxScoresElement))
            {
                return false;
            }

            predBoxesElement = NormalizeStateElement(predBoxesElement);
            boxScoresElement = NormalizeStateElement(boxScoresElement);
            return true;
        }

        private static JsonElement NormalizeStateElement(JsonElement element)
        {
            var current = element;

            while (current.ValueKind == JsonValueKind.Object &&
                   current.TryGetProperty("value", out var valueElement))
            {
                current = valueElement;
            }

            if (current.ValueKind == JsonValueKind.String)
            {
                var rawText = current.GetString();
                if (!string.IsNullOrWhiteSpace(rawText) &&
                    (rawText.TrimStart().StartsWith("[", StringComparison.Ordinal) ||
                     rawText.TrimStart().StartsWith("{", StringComparison.Ordinal)))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(rawText);
                        return document.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            return current.Clone();
        }

        private static bool TryParseFloatValue(JsonElement element, out float value)
        {
            value = 0f;
            var normalizedElement = NormalizeStateElement(element);

            if (normalizedElement.ValueKind == JsonValueKind.Number)
            {
                value = normalizedElement.GetSingle();
                return true;
            }

            if (normalizedElement.ValueKind == JsonValueKind.String &&
                float.TryParse(
                    normalizedElement.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryCollectBoxVectors(JsonElement element, List<float[]> boxes)
        {
            var normalizedElement = NormalizeStateElement(element);

            if (normalizedElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            if (TryReadBoxVector(normalizedElement, out var box))
            {
                boxes.Add(box);
                return true;
            }

            var foundAny = false;
            foreach (var child in normalizedElement.EnumerateArray())
            {
                foundAny |= TryCollectBoxVectors(child, boxes);
            }

            return foundAny;
        }

        private static bool TryReadBoxVector(JsonElement element, out float[] box)
        {
            box = Array.Empty<float>();
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 4)
            {
                return false;
            }

            var values = new float[4];
            var index = 0;

            foreach (var child in element.EnumerateArray())
            {
                if (!TryParseFloatValue(child, out var value))
                {
                    return false;
                }

                values[index++] = value;
            }

            box = values;
            return true;
        }

        private static bool TryCollectScalarFloats(JsonElement element, List<float> values)
        {
            var normalizedElement = NormalizeStateElement(element);

            if (TryParseFloatValue(normalizedElement, out var scalarValue))
            {
                values.Add(scalarValue);
                return true;
            }

            if (normalizedElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var foundAny = false;
            foreach (var child in normalizedElement.EnumerateArray())
            {
                foundAny |= TryCollectScalarFloats(child, values);
            }

            return foundAny;
        }

        private static bool TryGetImageDimensions(JsonElement element, out int width, out int height)
        {
            width = 0;
            height = 0;

            var normalizedElement = NormalizeStateElement(element);

            if (normalizedElement.ValueKind == JsonValueKind.Object &&
                normalizedElement.TryGetProperty("width", out var widthElement) &&
                normalizedElement.TryGetProperty("height", out var heightElement) &&
                widthElement.ValueKind == JsonValueKind.Number &&
                heightElement.ValueKind == JsonValueKind.Number)
            {
                width = widthElement.GetInt32();
                height = heightElement.GetInt32();
                return width > 0 && height > 0;
            }

            var dimensions = new List<int>();
            var current = normalizedElement;
            while (current.ValueKind == JsonValueKind.Array && current.GetArrayLength() > 0 && dimensions.Count < 3)
            {
                dimensions.Add(current.GetArrayLength());
                current = current[0];
            }

            if (dimensions.Count >= 2)
            {
                height = dimensions[0];
                width = dimensions[1];
                return width > 0 && height > 0;
            }

            return false;
        }

        private static float[]? ClampNormalizedBox(float[] box)
        {
            if (box.Length != 4)
            {
                return null;
            }

            var xmin = Math.Clamp(box[0], 0f, 1f);
            var ymin = Math.Clamp(box[1], 0f, 1f);
            var xmax = Math.Clamp(box[2], 0f, 1f);
            var ymax = Math.Clamp(box[3], 0f, 1f);

            if (xmax <= xmin || ymax <= ymin)
            {
                return null;
            }

            return new[] { xmin, ymin, xmax, ymax };
        }

        private static List<DetectionCandidate> ApplyNonMaximumSuppression(
            IReadOnlyList<DetectionCandidate> candidates,
            float iouThreshold)
        {
            var ordered = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            var kept = new List<DetectionCandidate>();

            while (ordered.Count > 0)
            {
                var current = ordered[0];
                kept.Add(current);
                ordered.RemoveAt(0);

                ordered = ordered
                    .Where(candidate => CalculateIoU(current, candidate) <= iouThreshold)
                    .ToList();
            }

            return kept;
        }

        private static float CalculateIoU(DetectionCandidate left, DetectionCandidate right)
        {
            var intersectionXmin = Math.Max(left.Xmin, right.Xmin);
            var intersectionYmin = Math.Max(left.Ymin, right.Ymin);
            var intersectionXmax = Math.Min(left.Xmax, right.Xmax);
            var intersectionYmax = Math.Min(left.Ymax, right.Ymax);

            var intersectionWidth = Math.Max(0f, intersectionXmax - intersectionXmin);
            var intersectionHeight = Math.Max(0f, intersectionYmax - intersectionYmin);
            var intersectionArea = intersectionWidth * intersectionHeight;

            if (intersectionArea <= 0f)
            {
                return 0f;
            }

            var leftArea = Math.Max(0f, left.Xmax - left.Xmin) * Math.Max(0f, left.Ymax - left.Ymin);
            var rightArea = Math.Max(0f, right.Xmax - right.Xmin) * Math.Max(0f, right.Ymax - right.Ymin);
            var unionArea = leftArea + rightArea - intersectionArea;

            return unionArea <= 0f ? 0f : intersectionArea / unionArea;
        }

        private static AIDetectionBoxResponse? ConvertCandidateToDetection(
            DetectionCandidate candidate,
            float scale,
            int? originalWidth,
            int? originalHeight)
        {
            if (scale <= 0f)
            {
                return null;
            }

            var pixelXmin = candidate.Xmin / scale * GECO2_CANONICAL_INPUT_SIZE;
            var pixelYmin = candidate.Ymin / scale * GECO2_CANONICAL_INPUT_SIZE;
            var pixelXmax = candidate.Xmax / scale * GECO2_CANONICAL_INPUT_SIZE;
            var pixelYmax = candidate.Ymax / scale * GECO2_CANONICAL_INPUT_SIZE;

            var xmin = Math.Max(0, (int)Math.Floor(pixelXmin));
            var ymin = Math.Max(0, (int)Math.Floor(pixelYmin));
            var xmax = Math.Max(xmin + 1, (int)Math.Ceiling(pixelXmax));
            var ymax = Math.Max(ymin + 1, (int)Math.Ceiling(pixelYmax));

            if (originalWidth.HasValue && originalWidth.Value > 0)
            {
                xmin = Math.Min(xmin, originalWidth.Value - 1);
                xmax = Math.Min(xmax, originalWidth.Value - 1);
            }

            if (originalHeight.HasValue && originalHeight.Value > 0)
            {
                ymin = Math.Min(ymin, originalHeight.Value - 1);
                ymax = Math.Min(ymax, originalHeight.Value - 1);
            }

            if (xmax <= xmin || ymax <= ymin)
            {
                return null;
            }

            return new AIDetectionBoxResponse
            {
                Xmin = xmin,
                Ymin = ymin,
                Xmax = xmax,
                Ymax = ymax,
                Confidence = MathF.Round(candidate.Score, 4)
            };
        }

        private sealed record ParsedDetectionResult(
            string? ResultImageUrl,
            int Count,
            List<AIDetectionBoxResponse> Detections,
            AIDetectionDiagnosticsResponse Diagnostics);

        private sealed record PredictionSubmissionResult(
            string EventId,
            string Endpoint);

        private sealed record DetectionCandidate(
            float Xmin,
            float Ymin,
            float Xmax,
            float Ymax,
            float Score);

        private static string BuildUpstreamConnectivityMessage(HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return ex.Message;
            }

            var detail = ex.InnerException?.Message ?? ex.Message;
            var combinedDetail = string.Join(" | ", new[] { ex.Message, ex.InnerException?.Message }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (combinedDetail.Contains("SSL", StringComparison.OrdinalIgnoreCase))
            {
                return "AI detection could not establish a secure SSL connection to an upstream service. " +
                       "If this only happens in local development, enable AI:AllowUnsafeSslForDevelopment in appsettings.Development.json and retry.";
            }

            return $"AI detection could not reach the required upstream resource: {detail}";
        }

        private static string? NormalizeHuggingFaceFileUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return null;
            }

            if (rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return rawUrl;
            }

            if (rawUrl.StartsWith($"{GRADIO_API_PREFIX}/file=", StringComparison.OrdinalIgnoreCase))
            {
                return $"{HF_SPACE_URL}{rawUrl}";
            }

            if (rawUrl.StartsWith("gradio_api/file=", StringComparison.OrdinalIgnoreCase))
            {
                return $"{HF_SPACE_URL}/{rawUrl}";
            }

            if (rawUrl.StartsWith("/file=", StringComparison.OrdinalIgnoreCase))
            {
                return $"{HF_SPACE_URL}{GRADIO_API_PREFIX}{rawUrl}";
            }

            if (rawUrl.StartsWith("file=", StringComparison.OrdinalIgnoreCase))
            {
                return $"{HF_SPACE_URL}{GRADIO_API_PREFIX}/{rawUrl}";
            }

            var normalized = rawUrl.StartsWith("/") ? rawUrl : $"/{rawUrl}";
            if (!normalized.StartsWith(FILE_ENDPOINT_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"{FILE_ENDPOINT_PREFIX}{normalized}";
            }

            return $"{HF_SPACE_URL}{normalized}";
        }

        private static bool ImageUrlMatchesAssignment(string requestImageUrl, string canonicalImageUrl)
        {
            if (string.Equals(requestImageUrl.Trim(), canonicalImageUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Uri.TryCreate(requestImageUrl, UriKind.Absolute, out var requestUri))
            {
                if (Uri.TryCreate(canonicalImageUrl, UriKind.Absolute, out var canonicalUri))
                {
                    return Uri.Compare(
                        requestUri,
                        canonicalUri,
                        UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                        UriFormat.Unescaped,
                        StringComparison.OrdinalIgnoreCase) == 0;
                }

                return string.Equals(
                    requestUri.AbsolutePath,
                    NormalizeRelativePath(canonicalImageUrl),
                    StringComparison.OrdinalIgnoreCase);
            }

            if (Uri.TryCreate(canonicalImageUrl, UriKind.Absolute, out var canonicalAbsoluteUri))
            {
                return string.Equals(
                    NormalizeRelativePath(requestImageUrl),
                    canonicalAbsoluteUri.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                NormalizeRelativePath(requestImageUrl),
                NormalizeRelativePath(canonicalImageUrl),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRelativePath(string value)
        {
            var normalized = value.Trim();
            if (normalized.StartsWith("~", StringComparison.Ordinal))
            {
                normalized = normalized[1..];
            }

            normalized = normalized.Replace('\\', '/');
            normalized = normalized.TrimStart('/');

            return $"/{normalized}";
        }

        private static string ResolveLocalAssetPath(string assetPath)
        {
            var webRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var normalizedRelativePath = NormalizeRelativePath(assetPath).TrimStart('/');
            var candidatePath = Path.GetFullPath(
                Path.Combine(webRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!candidatePath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid local image path.");
            }

            return candidatePath;
        }

        private static string GuessContentType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".jpeg" => "image/jpeg",
                ".jpg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }

        #endregion

        #region Internal DTOs

        private sealed record PreparedImage(string FileName, byte[] Content, string ContentType);

        private sealed record SseEvent(string? EventName, string Data);

        private class GradioEventResponse
        {
            [JsonPropertyName("event_id")]
            public string? EventId { get; set; }
        }

        #endregion
    }
}
