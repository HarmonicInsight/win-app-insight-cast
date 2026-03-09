using InsightCommon.UI.Reference;

namespace InsightCast.Services;

/// <summary>
/// Cast 用の参照資料パーサー。
/// 既存の WorkingFolderService のパース機能を IReferenceMaterialParser インターフェースに適合させる。
/// </summary>
public class CastReferenceMaterialParser : DefaultReferenceMaterialParser
{
    public override bool IsSupportedFile(string path)
        => WorkingFolderService.IsSupportedFile(path);

    public override ReferenceFileType GetFileType(string path)
    {
        var castType = WorkingFolderService.GetFileType(path);
        return castType switch
        {
            Models.WorkingFolderFileType.Word => ReferenceFileType.Word,
            Models.WorkingFolderFileType.Excel => ReferenceFileType.Excel,
            Models.WorkingFolderFileType.Pdf => ReferenceFileType.Pdf,
            Models.WorkingFolderFileType.PowerPoint => ReferenceFileType.PowerPoint,
            Models.WorkingFolderFileType.Image => ReferenceFileType.Image,
            _ => ReferenceFileType.Unknown,
        };
    }

    public override ReferenceMaterial ParseFile(string filePath)
    {
        var castMaterial = WorkingFolderService.ParseFile(filePath);
        return new ReferenceMaterial
        {
            FileName = castMaterial.FileName,
            FilePath = castMaterial.FilePath,
            FileType = GetFileType(filePath),
            ContentText = castMaterial.ContentText,
            ParagraphCount = castMaterial.ParagraphCount,
            WordCount = castMaterial.WordCount,
            SheetCount = castMaterial.SheetCount,
            CellCount = castMaterial.CellCount,
            SlideCount = castMaterial.SlideCount,
        };
    }
}
