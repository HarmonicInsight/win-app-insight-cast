using System.Collections.Generic;
using System.Text.Json.Serialization;
using InsightCast.Services;

namespace InsightCast.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MotionType
    {
        None,
        Auto,
        ZoomIn,
        ZoomOut,
        PanLeft,
        PanRight,
        PanUp,
        PanDown
    }

    public static class MotionNames
    {
        public static Dictionary<MotionType, string> DisplayNames => new()
        {
            { MotionType.None, LocalizationService.GetString("Motion.Type.None") },
            { MotionType.Auto, LocalizationService.GetString("Motion.Type.Auto") },
            { MotionType.ZoomIn, LocalizationService.GetString("Motion.Type.ZoomIn") },
            { MotionType.ZoomOut, LocalizationService.GetString("Motion.Type.ZoomOut") },
            { MotionType.PanLeft, LocalizationService.GetString("Motion.Type.PanLeft") },
            { MotionType.PanRight, LocalizationService.GetString("Motion.Type.PanRight") },
            { MotionType.PanUp, LocalizationService.GetString("Motion.Type.PanUp") },
            { MotionType.PanDown, LocalizationService.GetString("Motion.Type.PanDown") },
        };
    }

    public static class MotionResolver
    {
        /// <summary>
        /// Resolves Auto motion type based on image aspect ratio and scene index.
        /// </summary>
        /// <param name="imageWidth">Image width in pixels.</param>
        /// <param name="imageHeight">Image height in pixels.</param>
        /// <param name="sceneIndex">Scene index for alternation.</param>
        /// <returns>Resolved concrete MotionType (never Auto).</returns>
        public static MotionType Resolve(int imageWidth, int imageHeight, int sceneIndex)
        {
            double ratio = (double)imageWidth / imageHeight;

            if (ratio > 1.4)
            {
                // Landscape: alternate left/right pan
                return sceneIndex % 2 == 0 ? MotionType.PanRight : MotionType.PanLeft;
            }

            if (ratio < 0.8)
            {
                // Portrait / tall: zoom in
                return MotionType.ZoomIn;
            }

            // Square-ish: alternate zoom in/out
            return sceneIndex % 2 == 0 ? MotionType.ZoomIn : MotionType.ZoomOut;
        }
    }
}
