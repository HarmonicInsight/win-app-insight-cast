using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InsightCast.Views
{
    public class AiImageRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string ModelId { get; set; } = "dall-e-3";
        public string Size { get; set; } = "1280x720";
        public string Quality { get; set; } = "standard";
    }

    public class AiImageResult
    {
        public bool Success { get; set; }
        public string? ImagePath { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AiImageModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class AiImageService
    {
        public static List<AiImageModel> AvailableModels { get; } = new()
        {
            new AiImageModel { Id = "dall-e-3", DisplayName = "DALL-E 3" }
        };

        public AiImageService() { }

        public Task<AiImageResult> GenerateImageAsync(AiImageRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new AiImageResult
            {
                Success = false,
                ErrorMessage = "AI image generation is not yet implemented."
            });
        }
    }
}
