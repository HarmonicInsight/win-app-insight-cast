using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;

namespace InsightCast.Services
{
    public class ImportedSlide
    {
        public int SlideNumber { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    public static class PptxImportService
    {
        public static List<ImportedSlide> Import(string filePath)
        {
            var slides = new List<ImportedSlide>();

            // Copy to temp file to avoid file lock issues
            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"import_{System.Guid.NewGuid():N}.pptx");
            System.IO.File.Copy(filePath, tempPath, true);

            try
            {
                return ImportFromPath(tempPath);
            }
            finally
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }

        private static List<ImportedSlide> ImportFromPath(string filePath)
        {
            var slides = new List<ImportedSlide>();
            using var doc = PresentationDocument.Open(filePath, false);
            var presentation = doc.PresentationPart?.Presentation;
            if (presentation?.SlideIdList == null) return slides;

            int slideNumber = 0;
            foreach (SlideId slideId in presentation.SlideIdList)
            {
                slideNumber++;
                var slidePart = (SlidePart)doc.PresentationPart!.GetPartById(slideId.RelationshipId!);

                slides.Add(new ImportedSlide
                {
                    SlideNumber = slideNumber,
                    Title = ExtractTitle(slidePart),
                    Content = ExtractBodyText(slidePart),
                    Notes = ExtractNotes(slidePart)
                });
            }
            return slides;
        }

        private static string ExtractTitle(SlidePart slidePart)
        {
            var shapes = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<Shape>();
            if (shapes == null) return "";

            foreach (var shape in shapes)
            {
                var ph = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
                    .GetFirstChild<PlaceholderShape>();
                if (ph?.Type != null &&
                    (ph.Type.Value == PlaceholderValues.Title ||
                     ph.Type.Value == PlaceholderValues.CenteredTitle))
                {
                    return ExtractText(shape.TextBody);
                }
            }

            // Fallback: first shape with text
            foreach (var shape in shapes)
            {
                var text = ExtractText(shape.TextBody);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return "";
        }

        private static string ExtractBodyText(SlidePart slidePart)
        {
            var shapes = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<Shape>();
            if (shapes == null) return "";

            var bodyTexts = new List<string>();
            foreach (var shape in shapes)
            {
                var ph = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
                    .GetFirstChild<PlaceholderShape>();
                if (ph?.Type != null &&
                    (ph.Type.Value == PlaceholderValues.Title ||
                     ph.Type.Value == PlaceholderValues.CenteredTitle))
                    continue;

                var text = ExtractText(shape.TextBody);
                if (!string.IsNullOrWhiteSpace(text))
                    bodyTexts.Add(text);
            }
            return string.Join("\n", bodyTexts);
        }

        private static string ExtractNotes(SlidePart slidePart)
        {
            var notesPart = slidePart.NotesSlidePart;
            if (notesPart == null) return "";

            var shapes = notesPart.NotesSlide?.CommonSlideData?.ShapeTree?.Elements<Shape>();
            if (shapes == null) return "";

            foreach (var shape in shapes)
            {
                var ph = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
                    .GetFirstChild<PlaceholderShape>();
                if (ph?.Type?.Value == PlaceholderValues.Body)
                    return ExtractText(shape.TextBody);
            }
            return "";
        }

        private static string ExtractText(TextBody? textBody)
        {
            if (textBody == null) return "";
            var texts = new List<string>();
            foreach (var para in textBody.Elements<D.Paragraph>())
            {
                var sb = new System.Text.StringBuilder();
                foreach (var child in para.ChildElements)
                {
                    if (child is D.Run run)
                        sb.Append(run.Text?.Text ?? "");
                    else if (child is D.Break)
                        sb.Append('\n');
                }
                texts.Add(sb.ToString());
            }
            return string.Join("\n", texts);
        }
    }
}
