using System;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using InsightCast.Models;
using D = DocumentFormat.OpenXml.Drawing;

namespace InsightCast.Services
{
    public static class PptxGeneratorService
    {
        private const long SlideWidth = 12192000L;  // 1920px at 96dpi
        private const long SlideHeight = 6858000L;  // 1080px at 96dpi

        public static string GeneratePptx(ChapterStructure chapters, string? outputPath = null)
        {
            outputPath ??= Path.Combine(Path.GetTempPath(), "InsightCast", $"generated_{Guid.NewGuid():N}.pptx");
            var dir = Path.GetDirectoryName(outputPath);
            if (dir != null) Directory.CreateDirectory(dir);

            using var presentation = PresentationDocument.Create(outputPath, PresentationDocumentType.Presentation);
            var presentationPart = presentation.AddPresentationPart();
            presentationPart.Presentation = new Presentation();

            var slideLayoutPart = CreateSlideLayout(presentationPart);
            var slideIdList = new SlideIdList();
            presentationPart.Presentation.SlideIdList = slideIdList;
            presentationPart.Presentation.SlideSize = new SlideSize { Cx = (int)SlideWidth, Cy = (int)SlideHeight };

            uint slideId = 256;
            int slideNumber = 0;

            // Title slide
            slideNumber++;
            AddSlide(presentationPart, slideLayoutPart, ref slideId,
                chapters.VideoTitle, "", "");

            // Chapter slides
            foreach (var chapter in chapters.Chapters)
            {
                slideNumber++;
                AddSlide(presentationPart, slideLayoutPart, ref slideId,
                    chapter.Title, chapter.Narration, chapter.ImageDescription);
            }

            presentationPart.Presentation.Save();
            return outputPath;
        }

        private static SlideLayoutPart CreateSlideLayout(PresentationPart presentationPart)
        {
            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
            slideMasterPart.SlideMaster = new SlideMaster(
                new CommonSlideData(new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new D.TransformGroup()))),
                new SlideMasterIdList());

            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
            slideLayoutPart.SlideLayout = new SlideLayout(
                new CommonSlideData(new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new D.TransformGroup()))));
            slideLayoutPart.SlideLayout.Save();

            slideMasterPart.SlideMaster.Save();

            var masterIdList = new SlideMasterIdList();
            masterIdList.Append(new SlideMasterId
            {
                Id = 2147483648u,
                RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
            });
            presentationPart.Presentation.SlideMasterIdList = masterIdList;

            return slideLayoutPart;
        }

        private static void AddSlide(PresentationPart presentationPart, SlideLayoutPart slideLayoutPart, ref uint slideId, string title, string notes, string imageDescription)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.AddPart(slideLayoutPart);

            var slide = new Slide(
                new CommonSlideData(
                    new ShapeTree(
                        new NonVisualGroupShapeProperties(
                            new NonVisualDrawingProperties { Id = 1, Name = "" },
                            new NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(new D.TransformGroup()),
                        CreateTitleShape(title, slideId))));

            slidePart.Slide = slide;

            // Add notes
            var notesContent = notes;
            if (!string.IsNullOrEmpty(imageDescription))
                notesContent += $"\n\n[Image: {imageDescription}]";

            if (!string.IsNullOrWhiteSpace(notesContent))
            {
                var notesSlidePart = slidePart.AddNewPart<NotesSlidePart>();
                notesSlidePart.NotesSlide = new NotesSlide(
                    new CommonSlideData(
                        new ShapeTree(
                            new NonVisualGroupShapeProperties(
                                new NonVisualDrawingProperties { Id = 1, Name = "" },
                                new NonVisualGroupShapeDrawingProperties(),
                                new ApplicationNonVisualDrawingProperties()),
                            new GroupShapeProperties(new D.TransformGroup()),
                            CreateNotesShape(notesContent))));
                notesSlidePart.NotesSlide.Save();
            }

            slidePart.Slide.Save();

            presentationPart.Presentation.SlideIdList!.Append(new SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(slidePart)
            });
        }

        private static Shape CreateTitleShape(string text, uint id)
        {
            return new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = id + 100, Name = "Title" },
                    new NonVisualShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new ShapeProperties(
                    new D.Transform2D(
                        new D.Offset { X = 457200, Y = 274638 },
                        new D.Extents { Cx = 8229600, Cy = 1143000 })),
                new TextBody(
                    new D.BodyProperties(),
                    new D.Paragraph(
                        new D.Run(
                            new D.RunProperties(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.Text1 }))
                            {
                                Language = "ja-JP",
                                FontSize = 2800
                            },
                            new D.Text(text)))));
        }

        private static Shape CreateNotesShape(string text)
        {
            return new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = 2, Name = "Notes" },
                    new NonVisualShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties(
                        new PlaceholderShape { Type = PlaceholderValues.Body, Index = 1 })),
                new ShapeProperties(),
                new TextBody(
                    new D.BodyProperties(),
                    new D.Paragraph(
                        new D.Run(
                            new D.RunProperties { Language = "ja-JP", FontSize = 1200 },
                            new D.Text(text)))));
        }
    }
}
