using System;

namespace InsightCast.Models
{
    public class ReferenceMaterial
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public WorkingFolderFileType FileType { get; set; }
        public string ContentText { get; set; } = "";

        public int ParagraphCount { get; set; }
        public int WordCount { get; set; }
        public int SheetCount { get; set; }
        public int CellCount { get; set; }
        public int SlideCount { get; set; }

        public int EstimatedTokens => ContentText.Length / 3;
    }
}
