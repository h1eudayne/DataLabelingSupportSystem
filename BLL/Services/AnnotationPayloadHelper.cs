using System.Text.Json;

namespace BLL.Services
{
    internal sealed class AnnotationCanvasPayload
    {
        public List<JsonElement> Annotations { get; init; } = new();
        public Dictionary<string, JsonElement> Checklist { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<JsonElement> DefaultFlags { get; init; } = new();
        public List<JsonElement> LockedAnnotations { get; init; } = new();
        public List<JsonElement> LockedDefaultFlags { get; init; } = new();
        public List<int> RestrictedLabelIds { get; init; } = new();
        public string? RelabelReason { get; init; }
    }

    internal static class AnnotationPayloadHelper
    {
        public const string FlagEnabledMarker = "__flag_enabled";

        public static AnnotationCanvasPayload Parse(string? annotationData)
        {
            if (string.IsNullOrWhiteSpace(annotationData))
            {
                return new AnnotationCanvasPayload();
            }

            try
            {
                using var document = JsonDocument.Parse(annotationData);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return new AnnotationCanvasPayload
                    {
                        Annotations = root.EnumerateArray().Select(CloneElement).ToList()
                    };
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new AnnotationCanvasPayload();
                }

                var checklist = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("__checklist", out var checklistElement) &&
                    checklistElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in checklistElement.EnumerateObject())
                    {
                        checklist[property.Name] = CloneElement(property.Value);
                    }
                }

                return new AnnotationCanvasPayload
                {
                    Annotations = ReadArray(root, "annotations"),
                    Checklist = checklist,
                    DefaultFlags = ReadArray(root, "__defaultFlags"),
                    LockedAnnotations = ReadArray(root, "__lockedAnnotations"),
                    LockedDefaultFlags = ReadArray(root, "__lockedDefaultFlags"),
                    RestrictedLabelIds = ReadIntArray(root, "__relabelLabelIds"),
                    RelabelReason = root.TryGetProperty("__relabelReason", out var reasonElement) &&
                                    reasonElement.ValueKind == JsonValueKind.String
                        ? reasonElement.GetString()
                        : null
                };
            }
            catch
            {
                return new AnnotationCanvasPayload();
            }
        }

        public static string Serialize(AnnotationCanvasPayload payload)
        {
            var serializedPayload = new Dictionary<string, object?>
            {
                ["annotations"] = payload.Annotations.Select(CloneElement).ToList(),
                ["__checklist"] = payload.Checklist.ToDictionary(
                    item => item.Key,
                    item => (object)item.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase),
                ["__defaultFlags"] = payload.DefaultFlags.Select(CloneElement).ToList(),
            };

            if (payload.LockedAnnotations.Count > 0)
            {
                serializedPayload["__lockedAnnotations"] = payload.LockedAnnotations
                    .Select(CloneElement)
                    .ToList();
            }

            if (payload.LockedDefaultFlags.Count > 0)
            {
                serializedPayload["__lockedDefaultFlags"] = payload.LockedDefaultFlags
                    .Select(CloneElement)
                    .ToList();
            }

            if (payload.RestrictedLabelIds.Count > 0)
            {
                serializedPayload["__relabelLabelIds"] = payload.RestrictedLabelIds
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(payload.RelabelReason))
            {
                serializedPayload["__relabelReason"] = payload.RelabelReason;
            }

            return JsonSerializer.Serialize(serializedPayload);
        }

        public static bool UsesLabel(AnnotationCanvasPayload payload, int labelId)
        {
            return GetEffectiveAnnotations(payload).Any(annotation => AnnotationHasLabel(annotation, labelId)) ||
                   GetEffectiveDefaultFlags(payload).Any(flag => FlagMatchesLabel(flag, labelId));
        }

        public static AnnotationCanvasPayload CreateRelabelPayload(
            AnnotationCanvasPayload payload,
            int labelId,
            string relabelReason)
        {
            var effectiveAnnotations = GetEffectiveAnnotations(payload);
            var effectiveDefaultFlags = GetEffectiveDefaultFlags(payload);
            var previousRestrictedLabelIds = payload.RestrictedLabelIds
                .Distinct()
                .ToHashSet();
            var nextRestrictedLabelIds = previousRestrictedLabelIds
                .Append(labelId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            var preservedEditableAnnotations = payload.Annotations
                .Where(annotation =>
                    previousRestrictedLabelIds.Any(restrictedId => AnnotationHasLabel(annotation, restrictedId)) &&
                    !AnnotationHasLabel(annotation, labelId))
                .Select(CloneElement)
                .ToList();

            var preservedEditableDefaultFlags = payload.DefaultFlags
                .Where(flag =>
                    IsFlagEnabledMarker(flag) ||
                    (previousRestrictedLabelIds.Any(restrictedId => FlagMatchesLabel(flag, restrictedId)) &&
                     !FlagMatchesLabel(flag, labelId)))
                .Select(CloneElement)
                .ToList();

            var updatedChecklist = payload.Checklist.ToDictionary(
                item => item.Key,
                item => CloneElement(item.Value),
                StringComparer.OrdinalIgnoreCase);

            updatedChecklist.Remove(labelId.ToString());

            return new AnnotationCanvasPayload
            {
                Annotations = DeduplicateElements(preservedEditableAnnotations),
                Checklist = updatedChecklist,
                DefaultFlags = DeduplicateElements(preservedEditableDefaultFlags),
                LockedAnnotations = DeduplicateElements(
                    effectiveAnnotations
                        .Where(annotation => !AnnotationHasLabel(annotation, labelId))
                        .Select(CloneElement)
                        .ToList()),
                LockedDefaultFlags = DeduplicateElements(
                    effectiveDefaultFlags
                        .Where(flag => !FlagMatchesLabel(flag, labelId))
                        .Select(CloneElement)
                        .ToList()),
                RestrictedLabelIds = nextRestrictedLabelIds,
                RelabelReason = relabelReason
            };
        }

        public static AnnotationCanvasPayload PrepareForSubmission(AnnotationCanvasPayload payload)
        {
            var finalAnnotations = payload.RestrictedLabelIds.Count > 0
                ? GetEffectiveAnnotations(payload)
                : payload.Annotations.Select(CloneElement).ToList();

            var finalDefaultFlags = payload.RestrictedLabelIds.Count > 0
                ? GetEffectiveDefaultFlags(payload)
                : payload.DefaultFlags.Select(CloneElement).ToList();

            return new AnnotationCanvasPayload
            {
                Annotations = DeduplicateElements(finalAnnotations),
                Checklist = payload.Checklist.ToDictionary(
                    item => item.Key,
                    item => CloneElement(item.Value),
                    StringComparer.OrdinalIgnoreCase),
                DefaultFlags = DeduplicateElements(
                    finalDefaultFlags.Where(flag => !IsFlagEnabledMarker(flag)).Select(CloneElement).ToList())
            };
        }

        public static List<JsonElement> GetEffectiveAnnotations(AnnotationCanvasPayload payload)
        {
            if (payload.RestrictedLabelIds.Count == 0)
            {
                return payload.Annotations.Select(CloneElement).ToList();
            }

            var referenceAnnotations = payload.LockedAnnotations.Count > 0
                ? payload.LockedAnnotations
                : payload.Annotations;

            var preservedAnnotations = referenceAnnotations
                .Where(annotation => !payload.RestrictedLabelIds.Any(restrictedId => AnnotationHasLabel(annotation, restrictedId)))
                .Select(CloneElement);

            var editableAnnotations = payload.Annotations.Select(CloneElement);

            return DeduplicateElements(preservedAnnotations.Concat(editableAnnotations).ToList());
        }

        public static List<JsonElement> GetEffectiveDefaultFlags(AnnotationCanvasPayload payload)
        {
            if (payload.RestrictedLabelIds.Count == 0)
            {
                return DeduplicateElements(
                    payload.DefaultFlags
                        .Where(flag => !IsFlagEnabledMarker(flag))
                        .Select(CloneElement)
                        .ToList());
            }

            var referenceFlags = payload.LockedDefaultFlags.Count > 0
                ? payload.LockedDefaultFlags
                : payload.DefaultFlags;

            var preservedFlags = referenceFlags
                .Where(flag =>
                    !IsFlagEnabledMarker(flag) &&
                    !payload.RestrictedLabelIds.Any(restrictedId => FlagMatchesLabel(flag, restrictedId)))
                .Select(CloneElement);

            var editableFlags = payload.DefaultFlags
                .Where(flag => !IsFlagEnabledMarker(flag))
                .Select(CloneElement);

            return DeduplicateElements(preservedFlags.Concat(editableFlags).ToList());
        }

        public static bool AnnotationHasLabel(JsonElement annotation, int labelId)
        {
            if (annotation.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return TryGetIntProperty(annotation, "labelId", out var currentLabelId) && currentLabelId == labelId ||
                   TryGetIntProperty(annotation, "classId", out var currentClassId) && currentClassId == labelId ||
                   TryGetIntProperty(annotation, "LabelId", out currentLabelId) && currentLabelId == labelId ||
                   TryGetIntProperty(annotation, "ClassId", out currentClassId) && currentClassId == labelId;
        }

        public static bool FlagMatchesLabel(JsonElement flag, int labelId)
        {
            return TryGetElementInt(flag, out var parsedId) && parsedId == labelId;
        }

        public static bool IsFlagEnabledMarker(JsonElement flag)
        {
            return flag.ValueKind == JsonValueKind.String &&
                   string.Equals(flag.GetString(), FlagEnabledMarker, StringComparison.OrdinalIgnoreCase);
        }

        private static List<JsonElement> ReadArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return new List<JsonElement>();
            }

            return element.EnumerateArray()
                .Select(CloneElement)
                .ToList();
        }

        private static List<int> ReadIntArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return new List<int>();
            }

            return element.EnumerateArray()
                .Select(item => TryGetElementInt(item, out var value) ? value : (int?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        private static bool TryGetIntProperty(JsonElement element, string propertyName, out int value)
        {
            value = default;
            return element.TryGetProperty(propertyName, out var propertyValue) &&
                   TryGetElementInt(propertyValue, out value);
        }

        private static bool TryGetElementInt(JsonElement element, out int value)
        {
            value = default;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), out value))
            {
                return true;
            }

            return false;
        }

        private static List<JsonElement> DeduplicateElements(List<JsonElement> elements)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<JsonElement>();

            foreach (var element in elements)
            {
                var raw = element.GetRawText();
                if (seen.Add(raw))
                {
                    result.Add(CloneElement(element));
                }
            }

            return result;
        }

        private static JsonElement CloneElement(JsonElement element)
        {
            return element.Clone();
        }
    }
}
