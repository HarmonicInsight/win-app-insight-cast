using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using P = DocumentFormat.OpenXml.Presentation;
using InsightCast.Services;

namespace InsightCast.Services;

/// <summary>
/// Converts Word (.docx), Excel (.xlsx), and PDF (.pdf) files to PPTX format.
/// Uses PowerPoint COM interop when available, with OpenXml fallback for Word/Excel.
/// </summary>
public class DocumentConverterService
{
    private readonly Action<string>? _log;

    /// <summary>Supported import extensions.</summary>
    public static readonly string[] SupportedExtensions = { ".pptx", ".docx", ".xlsx", ".pdf" };

    public DocumentConverterService(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// Checks if a file extension is a supported document type (non-PPTX) that needs conversion.
    /// </summary>
    public static bool NeedsConversion(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".docx" or ".xlsx" or ".pdf";
    }

    /// <summary>
    /// Builds a file dialog filter string for all supported document types.
    /// </summary>
    public static string GetFileFilter()
    {
        return LocalizationService.GetString("DocConvert.Filter");
    }

    /// <summary>
    /// Converts a document file to PPTX format.
    /// Returns the path to the generated PPTX file, or null on failure.
    /// </summary>
    public string? ConvertToPptx(string inputPath)
    {
        var ext = System.IO.Path.GetExtension(inputPath).ToLowerInvariant();
        var outputDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "insightcast_cache", "doc_convert",
            $"conv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var outputPath = System.IO.Path.Combine(outputDir,
            System.IO.Path.GetFileNameWithoutExtension(inputPath) + ".pptx");

        try
        {
            return ext switch
            {
                ".docx" => ConvertWordToPptx(inputPath, outputPath),
                ".xlsx" => ConvertExcelToPptx(inputPath, outputPath),
                ".pdf" => ConvertPdfToPptx(inputPath, outputPath),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Conversion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Word (.docx) → PPTX: Extract text content, create one slide per section/page.
    /// Uses DocumentFormat.OpenXml to read Word content.
    /// </summary>
    private string? ConvertWordToPptx(string docxPath, string outputPath)
    {
        _log?.Invoke(LocalizationService.GetString("DocConvert.Converting", "Word"));

        // Try COM interop first (best quality)
        var comResult = ConvertViaPowerPointCom(docxPath, outputPath);
        if (comResult != null) return comResult;

        // Fallback: OpenXml text extraction → PPTX slides
        _log?.Invoke(LocalizationService.GetString("DocConvert.Fallback"));

        var paragraphs = new List<string>();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docxPath, false))
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return null;

            foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var text = para.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                    paragraphs.Add(text);
            }
        }

        if (paragraphs.Count == 0) return null;

        // Group paragraphs into slides (~5 paragraphs per slide)
        var slides = ChunkParagraphs(paragraphs, 5);
        CreatePptxFromTextSlides(outputPath, slides, System.IO.Path.GetFileNameWithoutExtension(docxPath));

        _log?.Invoke(LocalizationService.GetString("DocConvert.Done", slides.Count));
        return outputPath;
    }

    /// <summary>
    /// Excel (.xlsx) → PPTX: Each sheet becomes a slide with table data as text.
    /// Uses ClosedXML.
    /// </summary>
    private string? ConvertExcelToPptx(string xlsxPath, string outputPath)
    {
        _log?.Invoke(LocalizationService.GetString("DocConvert.Converting", "Excel"));

        // Try COM interop first
        var comResult = ConvertViaPowerPointCom(xlsxPath, outputPath);
        if (comResult != null) return comResult;

        // Fallback: ClosedXML text extraction → PPTX slides
        _log?.Invoke(LocalizationService.GetString("DocConvert.Fallback"));

        var slides = new List<List<string>>();
        using (var workbook = new XLWorkbook(xlsxPath))
        {
            foreach (var ws in workbook.Worksheets)
            {
                var lines = new List<string> { $"📊 {ws.Name}" };
                var range = ws.RangeUsed();
                if (range == null) continue;

                int maxRows = Math.Min(range.RowCount(), 20); // Limit rows per slide
                int maxCols = Math.Min(range.ColumnCount(), 8);

                for (int r = 1; r <= maxRows; r++)
                {
                    var cells = new List<string>();
                    for (int c = 1; c <= maxCols; c++)
                    {
                        var cell = ws.Cell(range.FirstRow().RowNumber() + r - 1,
                                          range.FirstColumn().ColumnNumber() + c - 1);
                        cells.Add(cell.GetFormattedString());
                    }
                    lines.Add(string.Join("  |  ", cells));
                }
                slides.Add(lines);
            }
        }

        if (slides.Count == 0) return null;

        CreatePptxFromTextSlides(outputPath, slides, System.IO.Path.GetFileNameWithoutExtension(xlsxPath));
        _log?.Invoke(LocalizationService.GetString("DocConvert.Done", slides.Count));
        return outputPath;
    }

    /// <summary>
    /// PDF → PPTX: Uses PowerPoint COM interop (PowerPoint can open PDFs directly).
    /// </summary>
    private string? ConvertPdfToPptx(string pdfPath, string outputPath)
    {
        _log?.Invoke(LocalizationService.GetString("DocConvert.Converting", "PDF"));

        var comResult = ConvertViaPowerPointCom(pdfPath, outputPath);
        if (comResult != null) return comResult;

        // No fallback for PDF without PowerPoint — inform user
        _log?.Invoke(LocalizationService.GetString("DocConvert.PdfRequiresPpt"));
        return null;
    }

    /// <summary>
    /// Uses PowerPoint COM interop to open any supported file and save as PPTX.
    /// Works with Word, Excel, and PDF files when PowerPoint is installed.
    /// </summary>
    private string? ConvertViaPowerPointCom(string inputPath, string outputPath)
    {
        dynamic? pptApp = null;
        dynamic? presentation = null;

        try
        {
            var pptType = Type.GetTypeFromProgID("PowerPoint.Application");
            if (pptType == null) return null;

            pptApp = Activator.CreateInstance(pptType);
            if (pptApp == null) return null;

            string fullPath = System.IO.Path.GetFullPath(inputPath);

            // Open the file in PowerPoint (it handles format detection)
            presentation = pptApp.Presentations.Open(
                fullPath, /* ReadOnly */ true, /* Untitled */ false, /* WithWindow */ false);

            // Save as PPTX (ppSaveAsOpenXMLPresentation = 24)
            presentation.SaveAs(System.IO.Path.GetFullPath(outputPath), 24);

            _log?.Invoke(LocalizationService.GetString("DocConvert.ComSuccess", presentation.Slides.Count));
            return outputPath;
        }
        catch
        {
            return null; // COM failed, will use fallback
        }
        finally
        {
            try { if (presentation != null) { presentation.Close(); Marshal.ReleaseComObject(presentation); } } catch { }
            try { if (pptApp != null) { pptApp.Quit(); Marshal.ReleaseComObject(pptApp); } } catch { }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static List<List<string>> ChunkParagraphs(List<string> paragraphs, int perSlide)
    {
        var slides = new List<List<string>>();
        for (int i = 0; i < paragraphs.Count; i += perSlide)
        {
            slides.Add(paragraphs.Skip(i).Take(perSlide).ToList());
        }
        return slides;
    }

    /// <summary>
    /// Creates a simple PPTX file with text content on each slide.
    /// </summary>
    private void CreatePptxFromTextSlides(string outputPath, List<List<string>> slides, string title)
    {
        using var pres = PresentationDocument.Create(outputPath, PresentationDocumentType.Presentation);

        var presPart = pres.AddPresentationPart();
        presPart.Presentation = new Presentation();

        // Create slide master/layout (minimal)
        var slideMasterPart = presPart.AddNewPart<SlideMasterPart>();
        slideMasterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new TransformGroup()))));

        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        slideLayoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new TransformGroup()))));

        slideMasterPart.SlideMaster.Append(new SlideLayoutIdList(
            new SlideLayoutId { Id = 2147483649U, RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart) }));

        presPart.Presentation.Append(new SlideMasterIdList(
            new SlideMasterId { Id = 2147483648U, RelationshipId = presPart.GetIdOfPart(slideMasterPart) }));

        var slideIdList = new SlideIdList();
        presPart.Presentation.Append(slideIdList);
        presPart.Presentation.Append(new SlideSize { Cx = 12192000, Cy = 6858000 }); // 16:9
        presPart.Presentation.Append(new NotesSize { Cx = 6858000, Cy = 9144000 });

        uint slideId = 256;
        for (int i = 0; i < slides.Count; i++)
        {
            var slidePart = presPart.AddNewPart<SlidePart>();
            var slideContent = string.Join("\n", slides[i]);

            slidePart.Slide = CreateTextSlide(slideContent, i == 0 ? title : null);
            slidePart.AddPart(slideLayoutPart);

            slideIdList.Append(new SlideId
            {
                Id = slideId++,
                RelationshipId = presPart.GetIdOfPart(slidePart)
            });

            // Add slide text as speaker notes (will become narration)
            var notesSlidePart = slidePart.AddNewPart<NotesSlidePart>();
            notesSlidePart.NotesSlide = CreateNotesSlide(slideContent);
        }

        presPart.Presentation.Save();
    }

    private static Slide CreateTextSlide(string text, string? title)
    {
        var shapeTree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new TransformGroup()));

        uint shapeId = 2;

        // Title shape (if first slide)
        if (title != null)
        {
            shapeTree.Append(CreateTextShape(shapeId++, "Title", title,
                457200, 274638, 8229600, 1143000, 2800, true));
        }

        // Body text shape
        int yOffset = title != null ? 1600200 : 457200;
        shapeTree.Append(CreateTextShape(shapeId++, "Body", text,
            457200, yOffset, 8229600, title != null ? 4525963 : 5715000, 1800, false));

        return new Slide(new CommonSlideData(shapeTree));
    }

    private static DocumentFormat.OpenXml.Presentation.Shape CreateTextShape(uint id, string name, string text,
        int x, int y, int cx, int cy, int fontSize, bool bold)
    {
        var paragraphs = text.Split('\n').Select(line =>
            new DocumentFormat.OpenXml.Drawing.Paragraph(
                new DocumentFormat.OpenXml.Drawing.Run(
                    new RunProperties
                    {
                        Language = "ja-JP",
                        FontSize = fontSize,
                        Bold = bold ? true : null
                    },
                    new DocumentFormat.OpenXml.Drawing.Text(line)))).ToArray();

        var textBody = new P.TextBody(
            new BodyProperties { Wrap = TextWrappingValues.Square },
            new ListStyle());
        foreach (var p in paragraphs) textBody.Append(p);

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new Transform2D(
                    new Offset { X = x, Y = y },
                    new Extents { Cx = cx, Cy = cy }),
                new PresetGeometry(new AdjustValueList()) { Preset = ShapeTypeValues.Rectangle }),
            textBody);
    }

    private static NotesSlide CreateNotesSlide(string text)
    {
        return new NotesSlide(
            new CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new TransformGroup()),
                    new P.Shape(
                        new P.NonVisualShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 2, Name = "Notes" },
                            new P.NonVisualShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties(
                                new PlaceholderShape { Type = PlaceholderValues.Body })),
                        new P.ShapeProperties(),
                        new P.TextBody(
                            new BodyProperties(),
                            new ListStyle(),
                            new DocumentFormat.OpenXml.Drawing.Paragraph(
                                new DocumentFormat.OpenXml.Drawing.Run(
                                    new DocumentFormat.OpenXml.Drawing.Text(text))))))));
    }
}
