using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InsightCast.Models
{
    public class ChapterStructure
    {
        [JsonPropertyName("videoTitle")]
        public string VideoTitle { get; set; } = "";

        [JsonPropertyName("chapters")]
        public List<ChapterItem> Chapters { get; set; } = new();
    }

    public class ChapterItem
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("narration")]
        public string Narration { get; set; } = "";

        [JsonPropertyName("imageDescription")]
        public string ImageDescription { get; set; } = "";
    }
}
