namespace InsightCast.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

/// <summary>
/// Data extracted from a single PowerPoint slide.
/// </summary>
public class SlideData
{
    /// <summary>One-based slide number.</summary>
    public int SlideNumber { get; set; }

    /// <summary>Path to the exported PNG image of this slide (null if not exported).</summary>
    public string? ImagePath { get; set; }

    /// <summary>Speaker notes text for this slide.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Text content extracted from the slide body (titles, bullet points, text boxes).</summary>
    public string SlideText { get; set; } = string.Empty;
}

/// <summary>
/// Imports PowerPoint (.pptx) files, extracting speaker notes via OpenXml
/// and exporting slides as PNG images via COM interop with PowerPoint.
/// </summary>
public class PptxImporter
{
    private readonly Action<int, int, string>? _progressCallback;

    /// <summary>
    /// Creates a new PptxImporter.
    /// </summary>
    /// <param name="progressCallback">
    /// Optional callback invoked to report progress: (current, total, message).
    /// </param>
    public PptxImporter(Action<int, int, string>? progressCallback = null)
    {
        _progressCallback = progressCallback;
    }

    /// <summary>
    /// Checks whether required libraries and tools are available.
    /// </summary>
    /// <returns>
    /// A tuple of (isAvailable, message). isAvailable is true if OpenXml can be used.
    /// </returns>
    public (bool IsAvailable, string Message) CheckRequirements()
    {
        try
        {
            // Verify that OpenXml types are loadable
            var testType = typeof(PresentationDocument);
            if (testType == null)
            {
                return (false, "DocumentFormat.OpenXml assembly is not available.");
            }

            return (true, "OpenXml is available. Notes extraction is supported.");
        }
        catch (Exception ex)
        {
            return (false, $"OpenXml check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts speaker notes from all slides in a PowerPoint file using OpenXml.
    /// </summary>
    /// <param name="pptxPath">Path to the .pptx file.</param>
    /// <returns>List of SlideData with notes populated (ImagePath will be null).</returns>
    /// <exception cref="FileNotFoundException">If the .pptx file does not exist.</exception>
    public List<SlideData> ExtractNotes(string pptxPath)
    {
        if (!File.Exists(pptxPath))
        {
            throw new FileNotFoundException(
                $"PowerPoint file not found: {pptxPath}", pptxPath);
        }

        var slides = new List<SlideData>();

        using var presentationDocument = PresentationDocument.Open(pptxPath, false);
        var presentationPart = presentationDocument.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList == null)
        {
            return slides;
        }

        var slideIdList = presentationPart.Presentation.SlideIdList
            .Elements<SlideId>().ToList();

        for (int i = 0; i < slideIdList.Count; i++)
        {
            int slideNumber = i + 1;
            ReportProgress(slideNumber, slideIdList.Count,
                $"Extracting notes from slide {slideNumber}/{slideIdList.Count}...");

            string notes = string.Empty;
            string slideText = string.Empty;

            try
            {
                var slideId = slideIdList[i];
                string? relId = slideId.RelationshipId?.Value;

                if (relId != null)
                {
                    var slidePart = (SlidePart)presentationPart.GetPartById(relId);

                    // Extract visible text from the slide body
                    slideText = ExtractTextFromSlidePart(slidePart);

                    // Extract speaker notes
                    var notesSlidePart = slidePart.NotesSlidePart;
                    if (notesSlidePart != null)
                    {
                        notes = ExtractTextFromNotesSlidePart(notesSlidePart);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Warning: Failed to extract data from slide {slideNumber}: {ex.Message}");
            }

            slides.Add(new SlideData
            {
                SlideNumber = slideNumber,
                ImagePath = null,
                Notes = notes,
                SlideText = slideText
            });
        }

        return slides;
    }

    /// <summary>
    /// Exports PowerPoint slides as PNG images using COM interop with Microsoft PowerPoint.
    /// Requires PowerPoint to be installed on the system.
    /// </summary>
    /// <param name="pptxPath">Path to the .pptx file.</param>
    /// <param name="outputDir">Directory to export PNGs into.</param>
    /// <param name="width">Export width in pixels.</param>
    /// <returns>
    /// List of file paths to exported PNG images (sorted by slide number),
    /// or null if export failed.
    /// </returns>
    public List<string>? ExportPngsWin32(string pptxPath, string outputDir, int width = 1920)
    {
        if (!File.Exists(pptxPath))
        {
            Console.Error.WriteLine($"PowerPoint file not found: {pptxPath}");
            return null;
        }

        Directory.CreateDirectory(outputDir);

        dynamic? pptApp = null;
        dynamic? presentation = null;

        try
        {
            ReportProgress(0, 1, "Starting PowerPoint application...");

            // Create PowerPoint.Application via COM interop
            var pptType = Type.GetTypeFromProgID("PowerPoint.Application");
            if (pptType == null)
            {
                Console.Error.WriteLine(
                    "PowerPoint is not installed. Cannot export slides as PNG.");
                return null;
            }

            pptApp = Activator.CreateInstance(pptType);
            if (pptApp == null)
            {
                Console.Error.WriteLine("Failed to create PowerPoint application instance.");
                return null;
            }

            // Open presentation
            // Parameters: FileName, ReadOnly, Untitled, WithWindow
            string fullPath = Path.GetFullPath(pptxPath);
            presentation = pptApp.Presentations.Open(
                fullPath,
                /* ReadOnly */ true,
                /* Untitled */ false,
                /* WithWindow */ false);

            int slideCount = presentation.Slides.Count;
            ReportProgress(0, slideCount, $"Exporting {slideCount} slides as PNG...");

            // Export all slides as PNG
            // PowerPoint exports to a subfolder named after the file
            string exportDir = Path.Combine(outputDir, "slides_export");
            Directory.CreateDirectory(exportDir);

            // Export each slide individually for progress reporting
            var exportedPaths = new List<string>();

            for (int i = 1; i <= slideCount; i++)
            {
                ReportProgress(i, slideCount, $"Exporting slide {i}/{slideCount}...");

                string slideFileName = $"slide_{i:D4}.png";
                string slideExportPath = Path.Combine(exportDir, slideFileName);

                try
                {
                    dynamic slide = presentation.Slides[i];
                    // Export(Path, FilterName, ScaleWidth, ScaleHeight)
                    slide.Export(slideExportPath, "PNG", width);
                    exportedPaths.Add(slideExportPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Warning: Failed to export slide {i}: {ex.Message}");
                }
            }

            return exportedPaths;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PowerPoint COM interop failed: {ex.Message}");
            return null;
        }
        finally
        {
            // Clean up COM objects
            try
            {
                if (presentation != null)
                {
                    presentation.Close();
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(presentation);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"PPTX presentation cleanup: {ex.Message}"); }

            try
            {
                if (pptApp != null)
                {
                    pptApp.Quit();
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(pptApp);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"PPTX app cleanup: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Full import pipeline: extracts notes and exports slides as PNG.
    /// </summary>
    /// <param name="pptxPath">Path to the .pptx file.</param>
    /// <param name="outputDir">
    /// Output directory for exported files. If null, a temp directory is created.
    /// </param>
    /// <param name="width">Export width in pixels.</param>
    /// <returns>List of SlideData with both notes and image paths populated.</returns>
    public List<SlideData> ImportPptx(string pptxPath, string? outputDir = null, int width = 1920)
    {
        if (!File.Exists(pptxPath))
        {
            throw new FileNotFoundException(
                $"PowerPoint file not found: {pptxPath}", pptxPath);
        }

        // Use temp directory if none specified
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(
                Path.GetTempPath(),
                $"pptx_import_{Guid.NewGuid():N}");
        }

        Directory.CreateDirectory(outputDir);

        // Step 1: Extract notes using OpenXml
        ReportProgress(0, 2, "Extracting speaker notes...");
        var slides = ExtractNotes(pptxPath);

        // Step 2: Export PNGs using COM interop
        ReportProgress(1, 2, "Exporting slide images...");
        var pngPaths = ExportPngsWin32(pptxPath, outputDir, width);

        // Step 3: Match PNG paths to slides
        if (pngPaths != null)
        {
            for (int i = 0; i < slides.Count && i < pngPaths.Count; i++)
            {
                slides[i].ImagePath = pngPaths[i];
            }
        }

        ReportProgress(2, 2, "Import complete.");
        return slides;
    }

    /// <summary>
    /// Saves extracted slide data (notes and image paths) to a JSON file.
    /// </summary>
    /// <param name="slides">List of SlideData to save.</param>
    /// <param name="outputPath">Path to the output JSON file.</param>
    /// <param name="sourceName">Original PPTX file name for reference.</param>
    public void SaveNotesJson(List<SlideData> slides, string outputPath, string sourceName)
    {
        var data = new
        {
            source = sourceName,
            exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            slideCount = slides.Count,
            slides = slides.Select(s => new
            {
                slideNumber = s.SlideNumber,
                imagePath = s.ImagePath,
                notes = s.Notes,
                slideText = s.SlideText
            }).ToArray()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string json = JsonSerializer.Serialize(data, options);

        // Ensure output directory exists
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(outputPath, json, System.Text.Encoding.UTF8);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts visible text content from a slide (titles, subtitles, body text, text boxes).
    /// </summary>
    private static string ExtractTextFromSlidePart(SlidePart slidePart)
    {
        var slide = slidePart.Slide;
        if (slide == null)
        {
            return string.Empty;
        }

        var commonSlideData = slide.CommonSlideData;
        if (commonSlideData?.ShapeTree == null)
        {
            return string.Empty;
        }

        var textParts = new List<string>();

        foreach (var shape in commonSlideData.ShapeTree.Elements<Shape>())
        {
            var textBody = shape.TextBody;
            if (textBody == null)
            {
                continue;
            }

            // Extract text from all text-bearing shapes on the slide
            // (titles, subtitles, body, and general text boxes)
            var shapeTexts = new List<string>();

            foreach (var paragraph in textBody.Elements<DocumentFormat.OpenXml.Drawing.Paragraph>())
            {
                var paragraphTexts = new List<string>();

                foreach (var run in paragraph.Elements<DocumentFormat.OpenXml.Drawing.Run>())
                {
                    var textElement = run.GetFirstChild<DocumentFormat.OpenXml.Drawing.Text>();
                    if (textElement?.Text != null)
                    {
                        paragraphTexts.Add(textElement.Text);
                    }
                }

                if (paragraphTexts.Count > 0)
                {
                    shapeTexts.Add(string.Join("", paragraphTexts));
                }
            }

            if (shapeTexts.Count > 0)
            {
                textParts.Add(string.Join("\n", shapeTexts));
            }
        }

        return string.Join("\n", textParts).Trim();
    }

    /// <summary>
    /// Extracts the text content from a NotesSlidePart.
    /// </summary>
    private static string ExtractTextFromNotesSlidePart(NotesSlidePart notesSlidePart)
    {
        var notesSlide = notesSlidePart.NotesSlide;
        if (notesSlide == null)
        {
            return string.Empty;
        }

        // Get all text elements from the notes body
        var textParts = new List<string>();

        var commonSlideData = notesSlide.CommonSlideData;
        if (commonSlideData?.ShapeTree == null)
        {
            return string.Empty;
        }

        foreach (var shape in commonSlideData.ShapeTree.Elements<Shape>())
        {
            var textBody = shape.TextBody;
            if (textBody == null)
            {
                continue;
            }

            // Check if this is the notes placeholder (type = body or notes)
            bool isNotesShape = false;
            var nvSpPr = shape.NonVisualShapeProperties;
            if (nvSpPr?.ApplicationNonVisualDrawingProperties != null)
            {
                var ph = nvSpPr.ApplicationNonVisualDrawingProperties
                    .GetFirstChild<PlaceholderShape>();
                if (ph != null)
                {
                    // PlaceholderShape type 2 = Body (notes content)
                    var phType = ph.Type?.Value;
                    if (phType == PlaceholderValues.Body)
                    {
                        isNotesShape = true;
                    }
                }
            }

            if (!isNotesShape)
            {
                continue;
            }

            // Extract text from paragraphs
            foreach (var paragraph in textBody.Elements<DocumentFormat.OpenXml.Drawing.Paragraph>())
            {
                var paragraphTexts = new List<string>();

                foreach (var run in paragraph.Elements<DocumentFormat.OpenXml.Drawing.Run>())
                {
                    var textElement = run.GetFirstChild<DocumentFormat.OpenXml.Drawing.Text>();
                    if (textElement?.Text != null)
                    {
                        paragraphTexts.Add(textElement.Text);
                    }
                }

                if (paragraphTexts.Count > 0)
                {
                    textParts.Add(string.Join("", paragraphTexts));
                }
            }
        }

        return string.Join("\n", textParts).Trim();
    }

    /// <summary>
    /// Reports progress via the callback if one was provided.
    /// </summary>
    private void ReportProgress(int current, int total, string message)
    {
        _progressCallback?.Invoke(current, total, message);
    }
}
