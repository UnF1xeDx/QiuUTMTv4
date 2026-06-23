// by porog
// Android-compatible version - no WinForms, no System.Drawing, no ImageMagick
// Uses SkiaSharp and TextureWorkerSkia instead

using System.IO.Compression;
using System.Linq;
using System.Text;
using UndertaleModLib.Util;
using SkiaSharp;

EnsureDataLoaded();

// List available fonts
if (Data.Fonts.Count == 0)
{
    ScriptMessage("No fonts found in the data file.");
    return;
}

// Build font list for user selection
StringBuilder fontList = new StringBuilder();
fontList.AppendLine("Available fonts:");
for (int i = 0; i < Data.Fonts.Count; i++)
{
    if (Data.Fonts[i] != null && Data.Fonts[i].Name?.Content != null)
        fontList.AppendLine($"{i}: {Data.Fonts[i].Name.Content}");
}

string fontInput = SimpleTextInput("Font Editor", "Enter font name or index:\n\n" + fontList.ToString(), "fnt_maintext", false);
if (string.IsNullOrEmpty(fontInput))
    return;

// Resolve font from input
UndertaleFont font = null;
if (int.TryParse(fontInput, out int fontIndex) && fontIndex >= 0 && fontIndex < Data.Fonts.Count)
    font = Data.Fonts[fontIndex];
else
    font = Data.Fonts.ByName(fontInput);

if (font is null)
{
    ScriptMessage("Font not found.");
    return;
}

// Ask user what to do
string action = SimpleTextInput("Font Editor - " + font.Name.Content,
    "Enter action:\n1 = Export font to ZIP\n2 = Import font from ZIP", "1", false);

if (action == "1")
    ExportFontToZip(font);
else if (action == "2")
    ImportFontFromZip(font);
else
    ScriptMessage("Invalid action.");

// ===================== EXPORT =====================
void ExportFontToZip(UndertaleFont font)
{
    string exportDir = PromptChooseDirectory();
    if (exportDir is null)
        return;

    string zipPath = Path.Join(exportDir, font.Name.Content + ".zip");

    using (TextureWorkerSkia worker = new())
    using (SKBitmap fontSheetImg = worker.GetTextureFor(font.Texture, null))
    {
        using (FileStream fileStream = File.Create(zipPath))
        using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            StringBuilder extraCharData = new StringBuilder();
            bool saveExtraCharData = false;

            foreach (UndertaleFont.Glyph glyph in font.Glyphs)
            {
                if (glyph.SourceWidth > 0 && glyph.SourceHeight > 0)
                {
                    // Crop glyph from font sheet
                    using SKBitmap glyphBmp = new SKBitmap(glyph.SourceWidth, glyph.SourceHeight);
                    SKRectI cropRect = new SKRectI(glyph.SourceX, glyph.SourceY,
                        glyph.SourceX + glyph.SourceWidth, glyph.SourceY + glyph.SourceHeight);
                    fontSheetImg.ExtractSubset(glyphBmp, cropRect);

                    string fileName =
                        "char=" + glyph.Character
                        + ";shift=" + glyph.Shift
                        + ";offset=" + glyph.Offset
                        + ".png";

                    ZipArchiveEntry entry = archive.CreateEntry(fileName);
                    using (Stream entryStream = entry.Open())
                    using (SKImage img = SKImage.FromBitmap(glyphBmp))
                    using (SKData data = img.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        data.SaveTo(entryStream);
                    }
                }
                else
                {
                    saveExtraCharData = true;
                    string appendix =
                        "char=" + glyph.Character
                        + ";width=0;height=0"
                        + ";shift=" + glyph.Shift
                        + ";offset=" + glyph.Offset
                        + "\n";
                    extraCharData.Append(appendix);
                }
            }

            if (saveExtraCharData)
            {
                extraCharData.Length = extraCharData.Length - 1; // remove trailing newline
                ZipArchiveEntry entry = archive.CreateEntry("otherletters.csv");
                using (StreamWriter writer = new StreamWriter(entry.Open()))
                {
                    byte[] content = Encoding.ASCII.GetBytes(extraCharData.ToString());
                    writer.BaseStream.Write(content, 0, content.Length);
                }
            }
        }
    }

    ScriptMessage("Exported successfully to:\n" + zipPath);
}

// ===================== IMPORT =====================
void ImportFontFromZip(UndertaleFont font)
{
    string importDir = PromptChooseDirectory();
    if (importDir is null)
        return;

    // Find the ZIP file
    string[] zipFiles = Directory.GetFiles(importDir, "*.zip");
    if (zipFiles.Length == 0)
    {
        ScriptMessage("No ZIP files found in the selected directory.");
        return;
    }

    string zipPath;
    if (zipFiles.Length == 1)
    {
        zipPath = zipFiles[0];
    }
    else
    {
        string fileList = string.Join("\n", zipFiles.Select((f, i) => $"{i}: {Path.GetFileName(f)}"));
        string selection = SimpleTextInput("Select ZIP", "Enter index of ZIP file:\n" + fileList, "0", false);
        if (!int.TryParse(selection, out int idx) || idx < 0 || idx >= zipFiles.Length)
        {
            ScriptMessage("Invalid selection.");
            return;
        }
        zipPath = zipFiles[idx];
    }

    List<GlyphImportData> importData = new List<GlyphImportData>();

    using (ZipArchive archive = ZipFile.OpenRead(zipPath))
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string fileName = entry.FullName;
            if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                string[] deserialize = fileName.Replace(".png", "").Split(';');
                for (int i = 0; i < deserialize.Length; i++)
                    deserialize[i] = deserialize[i].Substring(1 + deserialize[i].IndexOf('='));

                ushort character = UInt16.Parse(deserialize[0]);
                short shift = Int16.Parse(deserialize[1]);
                short offset = Int16.Parse(deserialize[2]);

                SKBitmap bitmap = null;
                using (Stream stream = entry.Open())
                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    using (SKData skData = SKData.Create(ms))
                        bitmap = SKBitmap.Decode(skData);
                }

                GlyphImportData glyphData = new GlyphImportData(character, bitmap, shift, offset);
                importData.Add(glyphData);
            }
            else if (fileName.Equals("otherletters.csv"))
            {
                string fileContent = null;
                using (Stream stream = entry.Open())
                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    fileContent = Encoding.ASCII.GetString(ms.ToArray());
                }

                foreach (string tuple in fileContent.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(tuple)) continue;
                    string[] deserialize = tuple.Split(';');
                    for (int i = 0; i < deserialize.Length; i++)
                        deserialize[i] = deserialize[i].Substring(1 + deserialize[i].IndexOf('='));

                    ushort character = UInt16.Parse(deserialize[0]);
                    short shift = Int16.Parse(deserialize[1]);
                    short offset = Int16.Parse(deserialize[2]);

                    GlyphImportData glyphData = new GlyphImportData(character, null, shift, offset);
                    importData.Add(glyphData);
                }
            }
        }
    }

    if (importData.Count == 0)
    {
        ScriptMessage("No glyph data found in the ZIP file.");
        return;
    }

    // Sort by height descending for packing
    importData.Sort((a, b) =>
    {
        int aHeight = a.Bitmap == null ? 0 : a.Bitmap.Height;
        int bHeight = b.Bitmap == null ? 0 : b.Bitmap.Height;
        return bHeight.CompareTo(aHeight);
    });

    // Generate font sheet image and glyph points
    using SKBitmap fontSheetImg = new SKBitmap(font.Texture.SourceWidth, font.Texture.SourceHeight);
    using (SKCanvas clearCanvas = new SKCanvas(fontSheetImg))
    {
        clearCanvas.Clear(SKColors.Transparent);
    }

    List<SKPoint> glyphPoints = new List<SKPoint>();
    int gap = 2;
    int xPos = gap;
    int yPos = gap;
    int rowMaxHeight = -1;

    using (SKCanvas fontCanvas = new SKCanvas(fontSheetImg))
    {
        foreach (GlyphImportData glyphData in importData)
        {
            bool glyphDrawable = glyphData.Bitmap != null;
            int glyphWidth = glyphDrawable ? glyphData.Bitmap.Width : 0;
            int glyphHeight = glyphDrawable ? glyphData.Bitmap.Height : 0;

            if (glyphHeight > rowMaxHeight)
                rowMaxHeight = glyphHeight;
            if (xPos + glyphWidth > fontSheetImg.Width)
            {
                xPos = gap;
                yPos += rowMaxHeight + gap;
                rowMaxHeight = -1;
            }
            glyphPoints.Add(new SKPoint(xPos, yPos));

            if (xPos + glyphWidth > fontSheetImg.Width
                || yPos + glyphHeight > fontSheetImg.Height)
            {
                ScriptMessage("All characters do not fit in the texture. Too many characters.");
                return;
            }

            if (glyphDrawable)
            {
                fontCanvas.DrawBitmap(glyphData.Bitmap, xPos, yPos);
            }

            xPos += glyphWidth + gap;
        }
    }

    // Set font sheet image onto the sprite sheet
    using (TextureWorkerSkia worker = new())
    {
        SKBitmap spriteSheetImg = worker.GetEmbeddedTexture(font.Texture.TexturePage);
        using (SKCanvas spriteCanvas = new SKCanvas(spriteSheetImg))
        {
            // Clear the old font area
            int x = font.Texture.SourceX;
            int y = font.Texture.SourceY;
            SKRectI clearRect = new SKRectI(x, y, x + fontSheetImg.Width, y + fontSheetImg.Height);
            spriteCanvas.DrawRect(clearRect, new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src });
            // Draw new font sheet
            spriteCanvas.DrawBitmap(fontSheetImg, x, y);
        }

        // Save the modified sprite sheet back
        using (SKImage img = SKImage.FromBitmap(spriteSheetImg))
        using (SKData data = img.Encode(SKEncodedImageFormat.Png, 100))
        using (MemoryStream ms = new MemoryStream())
        {
            data.SaveTo(ms);
            font.Texture.TexturePage.TextureData.Image = GMImage.FromPng(ms.ToArray());
            font.Texture.TargetX = 0;
            font.Texture.TargetY = 0;
        }
    }

    // Generate and set glyph data
    List<UndertaleFont.Glyph> glyphs = new List<UndertaleFont.Glyph>();
    for (int i = 0; i < importData.Count; i++)
    {
        UndertaleFont.Glyph glyph = new UndertaleFont.Glyph();
        GlyphImportData glyphData = importData[i];
        SKPoint glyphPoint = glyphPoints[i];

        glyph.Character = glyphData.Character;
        glyph.SourceX = (ushort)glyphPoint.X;
        glyph.SourceY = (ushort)glyphPoint.Y;
        glyph.SourceWidth = glyphData.Bitmap == null ? (ushort)0 : (ushort)glyphData.Bitmap.Width;
        glyph.SourceHeight = glyphData.Bitmap == null ? (ushort)0 : (ushort)glyphData.Bitmap.Height;
        glyph.Shift = glyphData.Shift;
        glyph.Offset = glyphData.Offset;

        glyphs.Add(glyph);
    }
    glyphs.Sort((x, y) => x.Character.CompareTo(y.Character));
    font.Glyphs.Clear();
    foreach (UndertaleFont.Glyph glyph in glyphs)
        font.Glyphs.Add(glyph);
    font.RangeStart = (ushort)glyphs[0].Character;
    font.RangeEnd = glyphs[glyphs.Count - 1].Character;

    // Dispose imported bitmaps
    foreach (var gd in importData)
        gd.Bitmap?.Dispose();

    ScriptMessage("Imported successfully from:\n" + zipPath);
}

class GlyphImportData
{
    public ushort Character { get; set; }
    public SKBitmap Bitmap { get; set; }
    public short Shift { get; set; }
    public short Offset { get; set; }
    public GlyphImportData(ushort character, SKBitmap bitmap, short shift, short offset)
    {
        this.Character = character;
        this.Bitmap = bitmap;
        this.Shift = shift;
        this.Offset = offset;
    }
}
