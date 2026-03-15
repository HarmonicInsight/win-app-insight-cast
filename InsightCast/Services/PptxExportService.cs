using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using InsightCast.Models;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace InsightCast.Services
{
    /// <summary>
    /// シーン一覧をPPTXファイルとしてエクスポートします。
    /// 各スライドにシーン画像を背景配置し、ナレーションテキストをノートに書き込みます。
    /// </summary>
    public class PptxExportService
    {
        private const int SLIDE_WIDTH = 12192000;   // 1280 * 9525
        private const int SLIDE_HEIGHT = 6858000;    // 720 * 9525

        public void Export(IList<Scene> scenes, string outputPath)
        {
            using var doc = PresentationDocument.Create(outputPath, PresentationDocumentType.Presentation);

            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new Presentation(
                new SlideSize { Cx = SLIDE_WIDTH, Cy = SLIDE_HEIGHT },
                new NotesSize { Cx = SLIDE_HEIGHT, Cy = SLIDE_WIDTH }
            );

            var slideIdList = new SlideIdList();
            presentationPart.Presentation.Append(slideIdList);

            uint slideId = 256;

            for (int i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                var slidePart = presentationPart.AddNewPart<SlidePart>($"rId{i + 2}");

                var slide = new Slide(new CommonSlideData(new ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new D.TransformGroup())
                )));

                if (scene.HasMedia && scene.MediaType == MediaType.Image && File.Exists(scene.MediaPath))
                {
                    AddImageToSlide(slidePart, slide, scene.MediaPath!);
                }

                slidePart.Slide = slide;

                if (scene.HasNarration)
                {
                    AddNotesToSlide(slidePart, scene.NarrationText!);
                }

                slideIdList.Append(new SlideId { Id = slideId++, RelationshipId = $"rId{i + 2}" });
            }

            presentationPart.Presentation.Save();
        }

        private static void AddImageToSlide(SlidePart slidePart, Slide slide, string imagePath)
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            var imageType = ext switch
            {
                ".png" => ImagePartType.Png,
                ".jpg" or ".jpeg" => ImagePartType.Jpeg,
                ".bmp" => ImagePartType.Bmp,
                ".gif" => ImagePartType.Gif,
                _ => ImagePartType.Png
            };

            var imagePart = slidePart.AddImagePart(imageType);
            using (var stream = File.OpenRead(imagePath))
            {
                imagePart.FeedData(stream);
            }

            var relId = slidePart.GetIdOfPart(imagePart);

            var picture = new P.Picture(
                new P.NonVisualPictureProperties(
                    new P.NonVisualDrawingProperties { Id = 2, Name = "Image" },
                    new P.NonVisualPictureDrawingProperties(new D.PictureLocks { NoChangeAspect = true }),
                    new ApplicationNonVisualDrawingProperties()),
                new P.BlipFill(
                    new D.Blip { Embed = relId },
                    new D.Stretch(new D.FillRectangle())),
                new P.ShapeProperties(
                    new D.Transform2D(
                        new D.Offset { X = 0, Y = 0 },
                        new D.Extents { Cx = SLIDE_WIDTH, Cy = SLIDE_HEIGHT }),
                    new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle })
            );

            slide.CommonSlideData!.ShapeTree!.Append(picture);
        }

        private static void AddNotesToSlide(SlidePart slidePart, string notesText)
        {
            var notesSlidePart = slidePart.AddNewPart<NotesSlidePart>();

            var body = new D.TextBody(new D.BodyProperties(), new D.ListStyle());

            var lines = notesText.Split('\n');
            foreach (var line in lines)
            {
                var text = new D.Text { Text = line };
                text.SetAttribute(new OpenXmlAttribute("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve"));
                body.Append(new D.Paragraph(new D.Run(text)));
            }

            var notesShape = new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 2, Name = "Notes Placeholder" },
                    new P.NonVisualShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties(
                        new PlaceholderShape { Type = PlaceholderValues.Body, Index = 1 })),
                new P.ShapeProperties(),
                body
            );

            notesSlidePart.NotesSlide = new NotesSlide(
                new CommonSlideData(new ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new D.TransformGroup()),
                    notesShape
                ))
            );
        }
    }
}
