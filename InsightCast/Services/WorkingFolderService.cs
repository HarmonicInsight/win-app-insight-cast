using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using InsightCast.Models;

namespace InsightCast.Services
{
    public static class WorkingFolderService
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".xlsx", ".xlsm", ".docx", ".pptx", ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif"
        };

        private const int MaxCellsPerSheet = 5_000;
        private const int MaxContentLength = 100_000;
        private const int MaxParagraphs = 300;
        private const int MaxContextLength = 150_000;

        public static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        public static WorkingFolderFileType GetFileType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".xlsx" or ".xlsm" => WorkingFolderFileType.Excel,
                ".docx" => WorkingFolderFileType.Word,
                ".pptx" => WorkingFolderFileType.PowerPoint,
                ".pdf" => WorkingFolderFileType.Pdf,
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => WorkingFolderFileType.Image,
                _ => WorkingFolderFileType.Unknown
            };
        }

        public static ReferenceMaterial ParseFile(string filePath)
        {
            var fileType = GetFileType(filePath);
            return fileType switch
            {
                WorkingFolderFileType.Excel => ParseExcel(filePath),
                WorkingFolderFileType.Word => ParseWord(filePath),
                WorkingFolderFileType.PowerPoint => ParsePptx(filePath),
                WorkingFolderFileType.Pdf => new ReferenceMaterial
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                    FileType = fileType,
                    ContentText = "[PDF file - text extraction not available]"
                },
                WorkingFolderFileType.Image => new ReferenceMaterial
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                    FileType = fileType,
                    ContentText = $"[Image: {Path.GetFileName(filePath)}]"
                },
                _ => throw new NotSupportedException($"Unsupported file type: {fileType}")
            };
        }

        private static ReferenceMaterial ParseExcel(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);

            var sb = new StringBuilder();
            int totalCells = 0;
            int sheetCount = 0;

            foreach (var sheet in workbook.Worksheets)
            {
                if (sb.Length >= MaxContentLength) break;

                sheetCount++;
                sb.AppendLine($"=== Sheet: {sheet.Name} ===");

                var usedRange = sheet.RangeUsed();
                if (usedRange == null)
                {
                    sb.AppendLine("(empty)");
                    continue;
                }

                int cellsInSheet = 0;
                int lastRow = usedRange.LastRow().RowNumber();
                int lastCol = usedRange.LastColumn().ColumnNumber();

                for (int row = 1; row <= lastRow; row++)
                {
                    if (cellsInSheet >= MaxCellsPerSheet) break;

                    var rowValues = new List<string>();
                    bool hasValue = false;

                    for (int col = 1; col <= lastCol && col <= 50; col++)
                    {
                        try
                        {
                            var val = sheet.Cell(row, col).GetFormattedString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                hasValue = true;
                                val = val.Trim();
                                if (val.Length > 100) val = val[..100] + "...";
                            }
                            rowValues.Add(val?.Trim() ?? "");
                        }
                        catch
                        {
                            rowValues.Add("");
                        }
                        cellsInSheet++;
                    }

                    if (hasValue)
                        sb.AppendLine(string.Join(" | ", rowValues));
                }

                totalCells += cellsInSheet;
                sb.AppendLine();
            }

            return new ReferenceMaterial
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileType = WorkingFolderFileType.Excel,
                ContentText = sb.ToString(),
                SheetCount = sheetCount,
                CellCount = totalCells
            };
        }

        private static ReferenceMaterial ParseWord(string filePath)
        {
            var sb = new StringBuilder();
            int paraCount = 0;
            int wordCount = 0;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = WordprocessingDocument.Open(stream, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null)
                {
                    return new ReferenceMaterial
                    {
                        FileName = Path.GetFileName(filePath),
                        FilePath = filePath,
                        FileType = WorkingFolderFileType.Word,
                        ContentText = "(empty document)"
                    };
                }

                foreach (var element in body.ChildElements)
                {
                    if (paraCount >= MaxParagraphs) break;

                    if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
                    {
                        var text = para.InnerText;
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                        var prefix = GetStylePrefix(styleId);

                        sb.AppendLine(CultureInfo.InvariantCulture, $"{prefix}{text}");
                        paraCount++;
                        wordCount += text.Split(new[] { ' ', '\t', '\u3000' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    }
                    else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                    {
                        sb.AppendLine("[Table]");
                        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                        {
                            if (paraCount >= MaxParagraphs) break;
                            var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                                .Select(c =>
                                {
                                    var t = c.InnerText.Trim();
                                    return t.Length > 80 ? t[..80] + "..." : t;
                                });
                            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
                            paraCount++;
                        }
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Error reading file: {ex.Message}]");
            }

            return new ReferenceMaterial
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileType = WorkingFolderFileType.Word,
                ContentText = sb.ToString(),
                ParagraphCount = paraCount,
                WordCount = wordCount
            };
        }

        private static ReferenceMaterial ParsePptx(string filePath)
        {
            var sb = new StringBuilder();
            int slideCount = 0;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = PresentationDocument.Open(stream, false);
                var presentation = doc.PresentationPart;
                if (presentation?.Presentation?.SlideIdList == null)
                {
                    return new ReferenceMaterial
                    {
                        FileName = Path.GetFileName(filePath),
                        FilePath = filePath,
                        FileType = WorkingFolderFileType.PowerPoint,
                        ContentText = "(empty presentation)"
                    };
                }

                foreach (var slideId in presentation.Presentation.SlideIdList.Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
                {
                    slideCount++;
                    var slidePart = (SlidePart)presentation.GetPartById(slideId.RelationshipId!);

                    sb.AppendLine($"--- Slide {slideCount} ---");

                    // Slide text
                    var slideText = slidePart.Slide?.InnerText;
                    if (!string.IsNullOrWhiteSpace(slideText))
                    {
                        if (slideText.Length > 500) slideText = slideText[..500] + "...";
                        sb.AppendLine(slideText);
                    }

                    // Speaker notes
                    var notesPart = slidePart.NotesSlidePart;
                    if (notesPart != null)
                    {
                        var notesText = notesPart.NotesSlide?.InnerText;
                        if (!string.IsNullOrWhiteSpace(notesText))
                        {
                            sb.AppendLine($"[Notes] {notesText}");
                        }
                    }

                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Error reading file: {ex.Message}]");
            }

            return new ReferenceMaterial
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileType = WorkingFolderFileType.PowerPoint,
                ContentText = sb.ToString(),
                SlideCount = slideCount
            };
        }

        private static string GetStylePrefix(string? styleId)
        {
            if (string.IsNullOrEmpty(styleId)) return "";
            var lower = styleId.ToLowerInvariant();
            if (lower.Contains("heading1")) return "[H1] ";
            if (lower.Contains("heading2")) return "[H2] ";
            if (lower.Contains("heading3")) return "[H3] ";
            if (lower.Contains("heading")) return "[H] ";
            if (lower.Contains("title")) return "[Title] ";
            return "";
        }

        public static string BuildReferenceContext(IReadOnlyList<ReferenceMaterial> materials)
        {
            if (materials.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"=== Reference Materials ({materials.Count} files) ===");
            sb.AppendLine();

            for (int i = 0; i < materials.Count; i++)
            {
                var m = materials[i];
                sb.AppendLine($"--- [{i + 1}] {m.FileName} ({m.FileType}) ---");

                var content = m.ContentText;
                var remaining = MaxContextLength - sb.Length;
                if (remaining <= 0)
                {
                    sb.AppendLine("[Context limit reached]");
                    break;
                }
                if (content.Length > remaining)
                {
                    sb.AppendLine(content[..remaining]);
                    sb.AppendLine("[Truncated]");
                    break;
                }

                sb.AppendLine(content);
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
