using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Seto90;

public sealed record BookExportReport(
    string MarkdownPath,
    string DocxPath,
    int Chapters,
    int Scenes,
    int Words,
    List<string> Warnings);

/// <summary>Exportador local, sin Word ni dependencias externas. El DOCX es OpenXML estandar
/// con Times New Roman 12, doble espacio, margenes de una pulgada y cabecera autor/titulo/pagina.</summary>
public static class StoryBookExporter
{
    static readonly UTF8Encoding Utf8NoBom = new(false);
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static BookExportReport Export(GameProject p, string projectRoot, string? baseName = null, bool strict = false)
    {
        var warnings = NarrativeTwin.ExportWarnings(p);
        if (strict && warnings.Count > 0)
            throw new InvalidOperationException("El manuscrito no esta listo para entrega: " + string.Join(" ", warnings));
        var book = p.StoryBook;
        var preferredName = baseName ?? (string.IsNullOrWhiteSpace(book.ShortTitle) ? book.Title : book.ShortTitle);
        var name = SafeName(preferredName);
        if (string.IsNullOrWhiteSpace(name)) name = SafeName(p.Id) is { Length: > 0 } id ? id : "manuscript";
        var dir = Path.Combine(Path.GetFullPath(projectRoot), "build", "book");
        Directory.CreateDirectory(dir);
        var md = Path.Combine(dir, name + ".md");
        var docx = Path.Combine(dir, name + ".docx");
        WriteTextAtomic(md, BuildMarkdown(p));
        WriteDocxAtomic(docx, p);
        return new(md, docx, book.Chapters.Count, book.Chapters.Sum(x => x.Scenes.Count), NarrativeTwin.WordCount(book), warnings);
    }

    public static string BuildMarkdown(GameProject p)
    {
        var book = p.StoryBook;
        var b = new StringBuilder();
        b.AppendLine("---");
        b.AppendLine($"title: \"{Yaml(book.Title)}\"");
        if (!string.IsNullOrWhiteSpace(book.Subtitle)) b.AppendLine($"subtitle: \"{Yaml(book.Subtitle)}\"");
        b.AppendLine($"author: \"{Yaml(book.Author)}\"");
        b.AppendLine($"language: \"{Yaml(book.Language)}\"");
        b.AppendLine($"wordCount: {NarrativeTwin.WordCount(book)}");
        b.AppendLine("generator: \"90s Engine - Libro Espejo\"");
        b.AppendLine("---");
        b.AppendLine();
        b.AppendLine($"# {book.Title}");
        if (!string.IsNullOrWhiteSpace(book.Subtitle)) b.AppendLine($"## {book.Subtitle}");
        b.AppendLine();
        if (!string.IsNullOrWhiteSpace(book.Author)) b.AppendLine($"**{book.Author}**");
        if (!string.IsNullOrWhiteSpace(book.Description)) { b.AppendLine(); b.AppendLine(book.Description.Trim()); }
        // Un titulo importado de un manuscrito ya suele traer su propia numeracion
        // ("Capitulo I: El apagon"): prefijar "Capitulo N:" encima produce un tartamudeo
        // en el entregable. Si el titulo ya se nombra capitulo, se respeta tal cual.
        static string ChapterHeading(int index, string title)
        {
            var t = (title ?? "").Trim();
            if (t.Length == 0) return $"Capitulo {index + 1}";
            var head = t.TrimStart('#', ' ').ToLowerInvariant();
            var namesItself = head.StartsWith("capitulo") || head.StartsWith("capítulo")
                || head.StartsWith("chapter") || head.StartsWith("cap.") || head.StartsWith("cap ");
            return namesItself ? t : $"Capitulo {index + 1}: {t}";
        }

        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var chapter = book.Chapters[i];
            b.AppendLine();
            b.AppendLine("---");
            b.AppendLine();
            b.AppendLine($"# {ChapterHeading(i, chapter.Title)}");
            b.AppendLine();
            for (var j = 0; j < chapter.Scenes.Count; j++)
            {
                if (j > 0) { b.AppendLine(); b.AppendLine("* * *"); b.AppendLine(); }
                b.AppendLine(chapter.Scenes[j].Prose.Trim());
                b.AppendLine();
            }
        }
        return b.ToString().TrimEnd() + Environment.NewLine;
    }

    static void WriteDocxAtomic(string path, GameProject p)
    {
        var temp = path + $".tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes());
                Add(zip, "_rels/.rels", RootRelationships());
                Add(zip, "docProps/core.xml", CoreProperties(p));
                Add(zip, "docProps/app.xml", AppProperties());
                Add(zip, "word/_rels/document.xml.rels", DocumentRelationships());
                Add(zip, "word/styles.xml", Styles());
                Add(zip, "word/header1.xml", Header(p.StoryBook));
                Add(zip, "word/document.xml", Document(p));
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    static XDocument Document(GameProject p)
    {
        var book = p.StoryBook;
        var body = new XElement(W + "body");
        body.Add(Paragraph(book.Title, "Title", center: true, indent: false));
        if (!string.IsNullOrWhiteSpace(book.Subtitle)) body.Add(Paragraph(book.Subtitle, center: true, indent: false));
        body.Add(Paragraph("", indent: false));
        body.Add(Paragraph("por", center: true, indent: false));
        body.Add(Paragraph(book.Author, center: true, indent: false));
        if (!string.IsNullOrWhiteSpace(book.Contact)) body.Add(Paragraph(book.Contact, center: true, indent: false));
        body.Add(PageBreak());
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var chapter = book.Chapters[i];
            if (i > 0) body.Add(PageBreak());
            body.Add(Paragraph($"CAPITULO {i + 1}", "Heading1", center: true, indent: false));
            body.Add(Paragraph(chapter.Title, "Heading1", center: true, indent: false));
            for (var j = 0; j < chapter.Scenes.Count; j++)
            {
                if (j > 0) body.Add(Paragraph("#", center: true, indent: false));
                foreach (var paragraph in Paragraphs(chapter.Scenes[j].Prose)) body.Add(Paragraph(paragraph));
            }
        }
        var (width, height) = book.PageSize == "a4" ? (11906, 16838) : (12240, 15840);
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "headerReference", new XAttribute(W + "type", "default"), new XAttribute(R + "id", "rId1")),
            new XElement(W + "pgSz", new XAttribute(W + "w", width), new XAttribute(W + "h", height)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1440), new XAttribute(W + "right", 1440), new XAttribute(W + "bottom", 1440), new XAttribute(W + "left", 1440), new XAttribute(W + "header", 720), new XAttribute(W + "footer", 720), new XAttribute(W + "gutter", 0))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R), body));
    }

    static XElement Paragraph(string text, string? style = null, bool center = false, bool indent = true)
    {
        var props = new XElement(W + "pPr");
        if (style != null) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", style)));
        if (center) props.Add(new XElement(W + "jc", new XAttribute(W + "val", "center")));
        if (indent) props.Add(new XElement(W + "ind", new XAttribute(W + "firstLine", 720)));
        return new XElement(W + "p", props,
            new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? "")));
    }

    static XElement PageBreak() => new(W + "p", new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page"))));
    static IEnumerable<string> Paragraphs(string prose) => Regex.Split((prose ?? "").Replace("\r\n", "\n").Trim(), @"\n\s*\n")
        .Select(x => Regex.Replace(x.Trim(), @"\s*\n\s*", " ")).Where(x => x.Length > 0);

    static XDocument Styles() => new(new XDeclaration("1.0", "UTF-8", "yes"),
        new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "docDefaults",
                new XElement(W + "rPrDefault", new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", "Times New Roman"), new XAttribute(W + "hAnsi", "Times New Roman")), new XElement(W + "sz", new XAttribute(W + "val", 24)), new XElement(W + "szCs", new XAttribute(W + "val", 24)))),
                new XElement(W + "pPrDefault", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", 480), new XAttribute(W + "lineRule", "auto"))))),
            Style("Normal", "Normal"),
            Style("Title", "Title", bold: true, size: 32),
            Style("Heading1", "Heading 1", bold: true, size: 24)));

    static XElement Style(string id, string name, bool bold = false, int size = 24) => new(W + "style",
        new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", id),
        new XElement(W + "name", new XAttribute(W + "val", name)),
        new XElement(W + "rPr", bold ? new XElement(W + "b") : null, new XElement(W + "sz", new XAttribute(W + "val", size))));

    static XDocument Header(StoryBookDef book)
    {
        var label = string.Join(" / ", new[] { book.Author, string.IsNullOrWhiteSpace(book.ShortTitle) ? book.Title : book.ShortTitle }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "hdr", new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "right"))),
                    new XElement(W + "r", new XElement(W + "t", label + " / ")),
                    new XElement(W + "fldSimple", new XAttribute(W + "instr", "PAGE"), new XElement(W + "r", new XElement(W + "t", "1"))))));
    }

    static XDocument ContentTypes() => new(new XElement(Ct + "Types",
        new XElement(Ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
        new XElement(Ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
        Override("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
        Override("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"),
        Override("/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"),
        Override("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"),
        Override("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml")));
    static XElement Override(string part, string type) => new(Ct + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));

    static XDocument RootRelationships() => new(new XElement(Rel + "Relationships",
        Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml"),
        Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
        Relationship("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml")));
    static XDocument DocumentRelationships() => new(new XElement(Rel + "Relationships",
        Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header", "header1.xml"),
        Relationship("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml")));
    static XElement Relationship(string id, string type, string target) => new(Rel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));

    static XDocument CoreProperties(GameProject p)
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace dcterms = "http://purl.org/dc/terms/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        return new(new XElement(cp + "coreProperties", new XAttribute(XNamespace.Xmlns + "cp", cp), new XAttribute(XNamespace.Xmlns + "dc", dc), new XAttribute(XNamespace.Xmlns + "dcterms", dcterms), new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XElement(dc + "title", p.StoryBook.Title), new XElement(dc + "creator", p.StoryBook.Author),
            new XElement(cp + "lastModifiedBy", "90s Engine - Libro Espejo"),
            new XElement(dcterms + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"), now),
            new XElement(dcterms + "modified", new XAttribute(xsi + "type", "dcterms:W3CDTF"), now)));
    }

    static XDocument AppProperties()
    {
        XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        return new(new XElement(ep + "Properties", new XAttribute(XNamespace.Xmlns + "vt", vt), new XElement(ep + "Application", "90s Engine")));
    }

    static void Add(ZipArchive zip, string path, XDocument document)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Utf8NoBom);
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    static void WriteTextAtomic(string path, string text)
    {
        var temp = path + $".tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try { File.WriteAllText(temp, text, Utf8NoBom); File.Move(temp, path, overwrite: true); }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    static string SafeName(string? value)
    {
        var clean = new string((value ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (clean.Contains("--", StringComparison.Ordinal)) clean = clean.Replace("--", "-", StringComparison.Ordinal);
        return clean.Trim('-');
    }
    static string Yaml(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
