using Pacos.Constants;
using Pacos.Models;
using Pacos.Services;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class OutputFileSenderTests
{
    private static readonly string[] AllMediaNames = ["a.jpg", "b.jpeg", "c.png", "d.gif", "e.webp", "f.mp4"];
    private static readonly string[] AllDocumentNames = ["g.pdf", "h.txt", "i.mp3", "j.zip"];
    private static readonly string[] SortedMediaNames = ["a.png", "b.mp4", "c.png"];
    private static readonly string[] SortedDocumentNames = ["m.pdf", "z.txt"];
    private static readonly string[] DroppedImageNames = ["img11.png", "img12.png"];
    private static readonly string[] DroppedDocumentNames = ["doc11.pdf", "doc12.pdf"];
    private static readonly string[] NsfwReportName = ["nsfw_report.pdf"];
    private static readonly string[] SmallVideoName = ["small.mp4"];
    private static readonly string[] HugeVideoName = ["huge.mp4"];
    private static readonly string[] SmallDocumentName = ["small.pdf"];
    private static readonly string[] HugeDocumentName = ["huge.zip"];
    private static readonly string[] HugePhotoName = ["huge.png"];
    private static readonly string[] ExactDocumentName = ["exact.zip"];
    private static readonly string[] WideImageName = ["wide.png"];
    private static readonly string[] NormalImageName = ["normal.png"];
    private static readonly string[] ReroutedMediaNames = ["clip.mp4", "normal.png"];
    private static readonly string[] ReroutedDocumentNames = ["doc.pdf", "wide.png"];
    private static readonly string[] ClipOnlyName = ["clip.mp4"];
    private static readonly string[] PicName = ["pic.png"];
    private static readonly string[] DocPdfName = ["doc.pdf"];
    private static readonly string[] DroppedPdfName = ["doc10.pdf"];
    private static readonly string[] NsfwWideImageName = ["nsfw_wide.png"];

    [Test]
    public void BuildPlan_WhenNoFiles_ShouldReturnEmptyGroups()
    {
        var (media, documents, droppedMedia, droppedDocuments, oversized) = OutputFileSender.BuildPlan([]);

        Assert.Multiple(() =>
        {
            Assert.That(media, Is.Empty);
            Assert.That(documents, Is.Empty);
            Assert.That(droppedMedia, Is.Empty);
            Assert.That(droppedDocuments, Is.Empty);
            Assert.That(oversized, Is.Empty);
        });
    }

    [Test]
    public void BuildPlan_ShouldTreatImagesAndVideosAsMediaAndEverythingElseAsDocuments()
    {
        var files = new[]
        {
            MakeFile("a.jpg"),
            MakeFile("b.jpeg"),
            MakeFile("c.png"),
            MakeFile("d.gif"),
            MakeFile("e.webp"),
            MakeFile("f.mp4"),
            MakeFile("g.pdf"),
            MakeFile("h.txt"),
            MakeFile("i.mp3"),
            MakeFile("j.zip"),
        };

        var (media, documents, droppedMedia, droppedDocuments, oversized) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EquivalentTo(AllMediaNames));
            Assert.That(documents.Select(d => d.FileName), Is.EquivalentTo(AllDocumentNames));
            Assert.That(droppedMedia, Is.Empty);
            Assert.That(droppedDocuments, Is.Empty);
            Assert.That(oversized, Is.Empty);
        });
    }

    [Test]
    public void BuildPlan_ShouldTreatExtensionsCaseInsensitively()
    {
        var (media, documents, _, _, _) = OutputFileSender.BuildPlan([MakeFile("PHOTO.JPG"), MakeFile("CLIP.MP4"), MakeFile("DOC.PDF")]);

        Assert.Multiple(() =>
        {
            Assert.That(media, Has.Count.EqualTo(2));
            Assert.That(documents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void BuildPlan_ShouldSortEachGroupAlphabetically()
    {
        var files = new[]
        {
            MakeFile("c.png"),
            MakeFile("a.png"),
            MakeFile("b.mp4"),
            MakeFile("z.txt"),
            MakeFile("m.pdf"),
        };

        var (media, documents, _, _, _) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(SortedMediaNames));
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(SortedDocumentNames));
        });
    }

    [Test]
    public void BuildPlan_ShouldTagVideosAndPhotosWithTheCorrectKind()
    {
        var (media, _, _, _, _) = OutputFileSender.BuildPlan([MakeFile("clip.mp4"), MakeFile("pic.png")]);

        Assert.Multiple(() =>
        {
            Assert.That(media[0].Kind, Is.EqualTo(OutputMediaKind.Video));
            Assert.That(media[1].Kind, Is.EqualTo(OutputMediaKind.Photo));
        });
    }

    [Test]
    public void BuildPlan_WhenMoreThanTenMedia_ShouldKeepFirstTenAndDropTheAlphabeticallyLast()
    {
        var files = Enumerable.Range(1, 12).Select(i => MakeFile($"img{i:D2}.png")).ToArray();

        var (media, _, droppedMedia, _, _) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(media, Has.Count.EqualTo(10));
            Assert.That(droppedMedia.Select(d => d.FileName), Is.EqualTo(DroppedImageNames));
        });
    }

    [Test]
    public void BuildPlan_WhenMoreThanTenDocuments_ShouldKeepFirstTenAndDropTheAlphabeticallyLast()
    {
        var files = Enumerable.Range(1, 12).Select(i => MakeFile($"doc{i:D2}.pdf")).ToArray();

        var (_, documents, _, droppedDocuments, _) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(documents, Has.Count.EqualTo(10));
            Assert.That(droppedDocuments.Select(d => d.FileName), Is.EqualTo(DroppedDocumentNames));
        });
    }

    [Test]
    public void BuildPlan_WhenBothGroupsOverflow_ShouldCapEachAtTenIndependently()
    {
        var files = Enumerable.Range(1, 12).Select(i => MakeFile($"img{i:D2}.png"))
            .Concat(Enumerable.Range(1, 12).Select(i => MakeFile($"doc{i:D2}.pdf")))
            .ToArray();

        var (media, documents, droppedMedia, droppedDocuments, _) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(media, Has.Count.EqualTo(10));
            Assert.That(documents, Has.Count.EqualTo(10));
            Assert.That(droppedMedia.Select(d => d.FileName), Is.EqualTo(DroppedImageNames));
            Assert.That(droppedDocuments.Select(d => d.FileName), Is.EqualTo(DroppedDocumentNames));
        });
    }

    [TestCase("nsfw_cat.png", true)]
    [TestCase("NSFW.png", true)]
    [TestCase("Nsfw.mp4", true)]
    [TestCase("holiday_NSFW_beach.webp", true)]
    [TestCase("cat.png", false)]
    [TestCase("safe_clip.mp4", false)]
    public void BuildPlan_ShouldFlagSpoilerWhenMediaNameContainsNsfw(string fileName, bool expected)
    {
        var (media, _, _, _, _) = OutputFileSender.BuildPlan([MakeFile(fileName)]);

        Assert.That(media, Has.Count.EqualTo(1));
        Assert.That(media[0].HasSpoiler, Is.EqualTo(expected));
    }

    [Test]
    public void BuildPlan_WhenDocumentNameContainsNsfw_ShouldStayInDocuments()
    {
        var (media, documents, _, _, _) = OutputFileSender.BuildPlan([MakeFile("nsfw_report.pdf")]);

        Assert.Multiple(() =>
        {
            Assert.That(media, Is.Empty);
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(NsfwReportName));
        });
    }

    [Test]
    public void BuildPlan_WhenVideoExceedsUploadLimit_ShouldDropItAsOversized()
    {
        var files = new[]
        {
            MakeFile("small.mp4", 1024),
            MakeFile("huge.mp4", Const.MaxTelegramFileSizeBytes + 1),
        };

        var (media, _, _, _, oversized) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(SmallVideoName));
            Assert.That(oversized.Select(f => f.FileName), Is.EqualTo(HugeVideoName));
        });
    }

    [Test]
    public void BuildPlan_WhenDocumentExceedsUploadLimit_ShouldDropItAsOversized()
    {
        var files = new[]
        {
            MakeFile("small.pdf", 1024),
            MakeFile("huge.zip", Const.MaxTelegramFileSizeBytes + 1),
        };

        var (_, documents, _, _, oversized) = OutputFileSender.BuildPlan(files);

        Assert.Multiple(() =>
        {
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(SmallDocumentName));
            Assert.That(oversized.Select(f => f.FileName), Is.EqualTo(HugeDocumentName));
        });
    }

    [Test]
    public void BuildPlan_WhenPhotoExceedsUploadLimit_ShouldKeepItForDownscaling()
    {
        var (media, _, _, _, oversized) = OutputFileSender.BuildPlan([MakeFile("huge.png", Const.MaxTelegramFileSizeBytes + 1)]);

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(HugePhotoName));
            Assert.That(oversized, Is.Empty);
        });
    }

    [Test]
    public void BuildPlan_WhenFileIsExactlyAtUploadLimit_ShouldKeepIt()
    {
        var (_, documents, _, _, oversized) = OutputFileSender.BuildPlan([MakeFile("exact.zip", Const.MaxTelegramFileSizeBytes)]);

        Assert.Multiple(() =>
        {
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(ExactDocumentName));
            Assert.That(oversized, Is.Empty);
        });
    }

    [Test]
    public void BuildPlan_WhenPredicateFlagsImage_ShouldRouteItToDocuments()
    {
        var (media, documents, _, _, _) = OutputFileSender.BuildPlan(
            [MakeFile("wide.png"), MakeFile("normal.png")],
            static f => f.FileName == "wide.png");

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(NormalImageName));
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(WideImageName));
        });
    }

    [Test]
    public void BuildPlan_WhenPredicateFlagsImage_ShouldLeaveOtherImagesAndVideosInMedia()
    {
        var files = new[]
        {
            MakeFile("wide.png"),
            MakeFile("normal.png"),
            MakeFile("clip.mp4"),
            MakeFile("doc.pdf"),
        };

        var (media, documents, _, _, _) = OutputFileSender.BuildPlan(files, static f => f.FileName == "wide.png");

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(ReroutedMediaNames));
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(ReroutedDocumentNames));
        });
    }

    [Test]
    public void BuildPlan_WhenPredicateReturnsTrueForVideo_ShouldStillKeepItInMedia()
    {
        var (media, documents, _, _, _) = OutputFileSender.BuildPlan(
            [MakeFile("clip.mp4"), MakeFile("pic.png")],
            static _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(ClipOnlyName));
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(PicName));
        });
    }

    [Test]
    public void BuildPlan_WhenFlaggedImageExceedsUploadLimit_ShouldDropItAsOversized()
    {
        var (media, documents, _, _, oversized) = OutputFileSender.BuildPlan(
            [MakeFile("wide.png", Const.MaxTelegramFileSizeBytes + 1)],
            static f => f.FileName == "wide.png");

        Assert.Multiple(() =>
        {
            Assert.That(media, Is.Empty);
            Assert.That(documents, Is.Empty);
            Assert.That(oversized.Select(f => f.FileName), Is.EqualTo(WideImageName));
        });
    }

    [Test]
    public void BuildPlan_WhenFlaggedImageJoinsDocuments_ShouldCountTowardTheDocumentCap()
    {
        var files = Enumerable.Range(1, 10).Select(i => MakeFile($"doc{i:D2}.pdf"))
            .Append(MakeFile("0wide.png"))
            .ToArray();

        var (media, documents, _, droppedDocuments, _) = OutputFileSender.BuildPlan(files, static f => f.FileName == "0wide.png");

        Assert.Multiple(() =>
        {
            Assert.That(media, Is.Empty);
            Assert.That(documents, Has.Count.EqualTo(10));
            Assert.That(droppedDocuments.Select(d => d.FileName), Is.EqualTo(DroppedPdfName));
        });
    }

    [Test]
    public void BuildPlan_WhenFlaggedImageIsNsfw_ShouldStillRouteItToDocuments()
    {
        var (media, documents, _, _, _) = OutputFileSender.BuildPlan([MakeFile("nsfw_wide.png")], static _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(media, Is.Empty);
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(NsfwWideImageName));
        });
    }

    [Test]
    public void BuildPlan_WithoutPredicate_ShouldRouteAllImagesToMedia()
    {
        var (media, documents, _, _, _) = OutputFileSender.BuildPlan([MakeFile("pic.png"), MakeFile("doc.pdf")]);

        Assert.Multiple(() =>
        {
            Assert.That(media.Select(m => m.File.FileName), Is.EqualTo(PicName));
            Assert.That(documents.Select(d => d.FileName), Is.EqualTo(DocPdfName));
        });
    }

    private static OutputFile MakeFile(string fileName) => new(fileName, []);

    private static OutputFile MakeFile(string fileName, int sizeBytes) => new(fileName, new byte[sizeBytes]);
}
