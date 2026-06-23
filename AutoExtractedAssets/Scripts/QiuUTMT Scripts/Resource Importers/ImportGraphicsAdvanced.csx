// ImportGraphics but it can also set sprite properties and import more types of files.

// Based off of ImportGraphics.csx by the UTMT team
// and ImportGraphicsWithParameters.csx by @DonavinDraws
// ImportGraphicsAdvanced-specific edits (extra formats and animation speed) made by CST1229

// Texture packer by Samuel Roy
// Uses code from https://github.com/mfascia/TexturePacker

// revision 2: fixed gif import not working unless the folder was named Sprites,
// fixed the default origin being Top Center instead of Top Left and
// reworded Is special type?'s boolean and the background import error message
// revision 3: added optional support for single-frame sprites if a frame number is not specified
// revision 4: added support for the texture handling refactor
// revision 5: handle breaking Magick.NET changes, disabled animation speed options in GMS1 games, hi-DPI support,
// renamed from ImportGraphicsWithParametersPlus to ImportGraphicsAdvanced
// revision 6: sprite texture items are now cropped, to save on texture page space and to fix sprite fonts
// revision 7: ported from Magick.NET to SkiaSharp, removed WinForms dependencies for Android compatibility

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UndertaleModLib.Util;
using UndertaleModLib.Models;
using SkiaSharp;

EnsureDataLoaded();

static bool importAsSprite = true;
static bool importFrameless = false;

string[] offsets = { "Top Left", "Top Center", "Top Right", "Center Left", "Center", "Center Right", "Bottom Left", "Bottom Center", "Bottom Right" };

string[] playbacks = { "Frames Per Second", "Frames Per Game Frame" };

static List<SKBitmap> imagesToCleanup = new();

float animSpd = 1;

bool isSpecial = false;
uint specialVer = 1;

string offresult;

int playback;

HashSet<string> spritesStartAt1 = new HashSet<string>();

string importFolder = CheckValidity();

string packDir = Path.Join(ExePath, "Packager");
Directory.CreateDirectory(packDir);

bool noMasksForBasicRectangles = Data.IsVersionAtLeast(2022, 9); // TODO: figure out the exact version, but this is pretty close

try
{
    string sourcePath = importFolder;
    string outName = Path.Join(packDir, "atlas.txt");
    int textureSize = 2048;
    int PaddingValue = 2;
    bool debug = false;
    Packer packer = new Packer();
    packer.Process(sourcePath, textureSize, PaddingValue, debug);
    packer.SaveAtlasses(outName);

    int lastTextPage = Data.EmbeddedTextures.Count - 1;
    int lastTextPageItem = Data.TexturePageItems.Count - 1;

    bool bboxMasks = Data.IsVersionAtLeast(2024, 6);
    Dictionary<UndertaleSprite, Node> maskNodes = new();

    // Import everything into UTMT
    string prefix = Path.Join(Path.GetDirectoryName(outName), Path.GetFileNameWithoutExtension(outName));
    int atlasCount = 0;
    OffsetResult();
    foreach (Atlas atlas in packer.Atlasses)
    {
        string atlasName = $"{prefix}{atlasCount:000}.png";
        using SKBitmap atlasImage = TextureWorkerSkia.ReadBGRAImageFromFile(atlasName);

        UndertaleEmbeddedTexture texture = new();
        texture.Name = new UndertaleString($"Texture {++lastTextPage}");
        texture.TextureData.Image = GMImage.FromSkiaImage(SKImage.FromBitmap(atlasImage)); // TODO: other formats?
        Data.EmbeddedTextures.Add(texture);
        foreach (Node n in atlas.Nodes)
        {
            if (n.Texture != null)
            {
                // Initalize values of this texture
                UndertaleTexturePageItem texturePageItem = new UndertaleTexturePageItem();
                texturePageItem.Name = new UndertaleString("PageItem " + ++lastTextPageItem);
                texturePageItem.SourceX = (ushort)n.Bounds.X;
                texturePageItem.SourceY = (ushort)n.Bounds.Y;
                texturePageItem.SourceWidth = (ushort)n.Bounds.Width;
                texturePageItem.SourceHeight = (ushort)n.Bounds.Height;
                texturePageItem.TargetX = (ushort)n.Texture.TargetX;
                texturePageItem.TargetY = (ushort)n.Texture.TargetY;
                texturePageItem.TargetWidth = (ushort)n.Bounds.Width;
                texturePageItem.TargetHeight = (ushort)n.Bounds.Height;
                texturePageItem.BoundingWidth = (ushort)n.Texture.BoundingWidth;
                texturePageItem.BoundingHeight = (ushort)n.Texture.BoundingHeight;
                texturePageItem.TexturePage = texture;

                // Add this texture to UMT
                Data.TexturePageItems.Add(texturePageItem);

                // String processing
                string stripped = Path.GetFileNameWithoutExtension(n.Texture.Source);

                SpriteType spriteType = GetSpriteType(n.Texture.Source);

                if (importAsSprite)
                {
                    if ((spriteType == SpriteType.Unknown) || (spriteType == SpriteType.Font))
                    {
                        spriteType = SpriteType.Sprite;
                    }
                }

                if (spriteType == SpriteType.Background)
                {
                    UndertaleBackground background = Data.Backgrounds.ByName(stripped);
                    if (background is not null)
                    {
                        background.Texture = texturePageItem;
                    }
                    else
                    {
                        // No background found, let's make one
                        UndertaleString backgroundUTString = Data.Strings.MakeString(stripped);
                        background = new UndertaleBackground();
                        background.Name = backgroundUTString;
                        background.Transparent = false;
                        background.Preload = false;
                        background.Texture = texturePageItem;
                        Data.Backgrounds.Add(background);
                    }
                    Project?.MarkAssetForExport(background);
                }
                else if (spriteType == SpriteType.Sprite)
                {
                    // Get sprite to add this texture to
                    string spriteName;
                    int lastUnderscore, frame;
                    try
                    {
                        lastUnderscore = stripped.LastIndexOf('_');
                        // check if the frame number is a valid string or not
                        Int32.Parse(stripped.Substring(lastUnderscore + 1));
                        spriteName = stripped.Substring(0, lastUnderscore);
                        frame = Int32.Parse(stripped.Substring(lastUnderscore + 1));
                    }
                    catch (Exception e)
                    {
                        if (!importFrameless)
                        {
                            continue;
                        }
                        spriteName = stripped;
                        frame = 0;
                    }

                    if (spritesStartAt1.Contains(spriteName))
                    {
                        frame--;
                    }

                    // Create TextureEntry object
                    UndertaleSprite.TextureEntry texentry = new UndertaleSprite.TextureEntry();
                    texentry.Texture = texturePageItem;

                    // Set values for new sprites
                    UndertaleSprite sprite = Data.Sprites.ByName(spriteName);
                    if (sprite is null)
                    {
                        UndertaleString spriteUTString = Data.Strings.MakeString(spriteName);
                        UndertaleSprite newSprite = new UndertaleSprite();
                        newSprite.Name = spriteUTString;
                        newSprite.Width = (uint)n.Texture.BoundingWidth;
                        newSprite.Height = (uint)n.Texture.BoundingHeight;
                        newSprite.MarginLeft = n.Texture.TargetX;
                        newSprite.MarginRight = n.Texture.TargetX + n.Bounds.Width - 1;
                        newSprite.MarginTop = n.Texture.TargetY;
                        newSprite.MarginBottom = n.Texture.TargetY + n.Bounds.Height - 1;
                        newSprite.GMS2PlaybackSpeedType = (AnimSpeedType)playback;
                        newSprite.GMS2PlaybackSpeed = animSpd;
                        newSprite.IsSpecialType = isSpecial;
                        newSprite.SVersion = specialVer;
                        switch (offresult)
                        {
                            case ("Top Left"):
                                newSprite.OriginX = 0;
                                newSprite.OriginY = 0;
                                break;
                            case ("Top Center"):
                                newSprite.OriginX = (int)(newSprite.Width / 2);
                                newSprite.OriginY = 0;
                                break;
                            case ("Top Right"):
                                newSprite.OriginX = (int)(newSprite.Width);
                                newSprite.OriginY = 0;
                                break;
                            case ("Center Left"):
                                newSprite.OriginX = 0;
                                newSprite.OriginY = (int)(newSprite.Height / 2);
                                break;
                            case ("Center"):
                                newSprite.OriginX = (int)(newSprite.Width / 2);
                                newSprite.OriginY = (int)(newSprite.Height / 2);
                                break;
                            case ("Center Right"):
                                newSprite.OriginX = (int)(newSprite.Width);
                                newSprite.OriginY = (int)(newSprite.Height / 2);
                                break;
                            case ("Bottom Left"):
                                newSprite.OriginX = 0;
                                newSprite.OriginY = (int)(newSprite.Height);
                                break;
                            case ("Bottom Center"):
                                newSprite.OriginX = (int)(newSprite.Width / 2);
                                newSprite.OriginY = (int)(newSprite.Height);
                                break;
                            case ("Bottom Right"):
                                newSprite.OriginX = (int)(newSprite.Width);
                                newSprite.OriginY = (int)(newSprite.Height);
                                break;
                        }
                        if (frame > 0)
                        {
                            for (int i = 0; i < frame; i++)
                                newSprite.Textures.Add(null);
                        }

                        // Only generate collision masks for sprites that need them (in newer GameMaker versions)
                        if (!noMasksForBasicRectangles ||
                            newSprite.SepMasks is not (UndertaleSprite.SepMaskType.AxisAlignedRect or UndertaleSprite.SepMaskType.RotatedRect))
                        {
                            // Generate mask later (when the current atlas is about to be unloaded)
                            maskNodes.Add(newSprite, n);
                        }

                        newSprite.Textures.Add(texentry);
                        Data.Sprites.Add(newSprite);
                        Project?.MarkAssetForExport(newSprite);
                        continue;
                    }

                    Project?.MarkAssetForExport(sprite);

                    if (frame > sprite.Textures.Count - 1)
                    {
                        while (frame > sprite.Textures.Count - 1)
                        {
                            sprite.Textures.Add(texentry);
                        }
                        continue;
                    }

                    sprite.Textures[frame] = texentry;
                    sprite.GMS2PlaybackSpeedType = (AnimSpeedType)playback;
                    sprite.GMS2PlaybackSpeed = animSpd;
                    sprite.IsSpecialType = isSpecial;
                    sprite.SVersion = specialVer;

                    // Update sprite dimensions
                    uint oldWidth = sprite.Width, oldHeight = sprite.Height;
                    sprite.Width = (uint)n.Texture.BoundingWidth;
                    sprite.Height = (uint)n.Texture.BoundingHeight;
                    bool changedSpriteDimensions = (oldWidth != sprite.Width || oldHeight != sprite.Height);

                    // Update origin
                    switch (offresult)
                    {
                        case ("Top Left"):
                            sprite.OriginX = 0;
                            sprite.OriginY = 0;
                            break;
                        case ("Top Center"):
                            sprite.OriginX = (int)(sprite.Width / 2);
                            sprite.OriginY = 0;
                            break;
                        case ("Top Right"):
                            sprite.OriginX = (int)(sprite.Width);
                            sprite.OriginY = 0;
                            break;
                        case ("Center Left"):
                            sprite.OriginX = 0;
                            sprite.OriginY = (int)(sprite.Height / 2);
                            break;
                        case ("Center"):
                            sprite.OriginX = (int)(sprite.Width / 2);
                            sprite.OriginY = (int)(sprite.Height / 2);
                            break;
                        case ("Center Right"):
                            sprite.OriginX = (int)(sprite.Width);
                            sprite.OriginY = (int)(sprite.Height / 2);
                            break;
                        case ("Bottom Left"):
                            sprite.OriginX = 0;
                            sprite.OriginY = (int)(sprite.Height);
                            break;
                        case ("Bottom Center"):
                            sprite.OriginX = (int)(sprite.Width / 2);
                            sprite.OriginY = (int)(sprite.Height);
                            break;
                        case ("Bottom Right"):
                            sprite.OriginX = (int)(sprite.Width);
                            sprite.OriginY = (int)(sprite.Height);
                            break;
                    }

                    // Grow bounding box depending on how much is trimmed
                    bool grewBoundingBox = false;
                    bool fullImageBbox = sprite.BBoxMode == 1;
                    bool manualBbox = sprite.BBoxMode == 2;
                    if (!manualBbox)
                    {
                        int marginLeft = fullImageBbox ? 0 : n.Texture.TargetX;
                        int marginRight = fullImageBbox ? ((int)sprite.Width - 1) : (n.Texture.TargetX + n.Bounds.Width - 1);
                        int marginTop = fullImageBbox ? 0 : n.Texture.TargetY;
                        int marginBottom = fullImageBbox ? ((int)sprite.Height - 1) : (n.Texture.TargetY + n.Bounds.Height - 1);
                        if (marginLeft < sprite.MarginLeft)
                        {
                            sprite.MarginLeft = marginLeft;
                            grewBoundingBox = true;
                        }
                        if (marginTop < sprite.MarginTop)
                        {
                            sprite.MarginTop = marginTop;
                            grewBoundingBox = true;
                        }
                        if (marginRight > sprite.MarginRight)
                        {
                            sprite.MarginRight = marginRight;
                            grewBoundingBox = true;
                        }
                        if (marginBottom > sprite.MarginBottom)
                        {
                            sprite.MarginBottom = marginBottom;
                            grewBoundingBox = true;
                        }
                    }

                    // Only generate collision masks for sprites that need them (in newer GameMaker versions)
                    if (!noMasksForBasicRectangles ||
                        sprite.SepMasks is not (UndertaleSprite.SepMaskType.AxisAlignedRect or UndertaleSprite.SepMaskType.RotatedRect) ||
                        sprite.CollisionMasks.Count > 0)
                    {
                        if ((bboxMasks && grewBoundingBox) ||
                            (sprite.SepMasks is UndertaleSprite.SepMaskType.Precise && sprite.CollisionMasks.Count == 0) ||
                            (!bboxMasks && changedSpriteDimensions))
                        {
                            // Use this node for the sprite's collision mask if the bounding box grew (or if no collision mask exists for a precise sprite)
                            maskNodes[sprite] = n;
                        }
                    }
                }
            }
        }

        // Update masks for when bounding box masks are enabled
        foreach ((UndertaleSprite maskSpr, Node maskNode) in maskNodes)
        {
            // Generate collision mask using either bounding box or sprite dimensions
            maskSpr.CollisionMasks.Clear();
            maskSpr.CollisionMasks.Add(maskSpr.NewMaskEntry(Data));
            (int maskWidth, int maskHeight) = maskSpr.CalculateMaskDimensions(Data);
            int maskStride = ((maskWidth + 7) / 8) * 8;

            BitArray maskingBitArray = new BitArray(maskStride * maskHeight);
            for (int y = 0; y < maskHeight && y < maskNode.Bounds.Height; y++)
            {
                for (int x = 0; x < maskWidth && x < maskNode.Bounds.Width; x++)
                {
                    SKColor pixelColor = atlasImage.GetPixel(x + maskNode.Bounds.X, y + maskNode.Bounds.Y);
                    if (bboxMasks)
                    {
                        maskingBitArray[(y * maskStride) + x] = (pixelColor.Alpha > 0);
                    }
                    else
                    {
                        maskingBitArray[((y + maskNode.Texture.TargetY) * maskStride) + x + maskNode.Texture.TargetX] = (pixelColor.Alpha > 0);
                    }
                }
            }
            BitArray tempBitArray = new BitArray(maskingBitArray.Length);
            for (int i = 0; i < maskingBitArray.Length; i += 8)
            {
                for (int j = 0; j < 8; j++)
                {
                    tempBitArray[j + i] = maskingBitArray[-(j - 7) + i];
                }
            }

            int numBytes = maskingBitArray.Length / 8;
            byte[] bytes = new byte[numBytes];
            tempBitArray.CopyTo(bytes, 0);
            for (int i = 0; i < bytes.Length; i++)
                maskSpr.CollisionMasks[0].Data[i] = bytes[i];
        }
        maskNodes.Clear();

        // Increment atlas
        atlasCount++;
    }

    HideProgressBar();
    ScriptMessage("Import Complete!");
}
finally
{
    foreach (SKBitmap img in imagesToCleanup)
    {
        img.Dispose();
    }
}

public class TextureInfo
{
    public string Source;
    public int Width;
    public int Height;
    public int TargetX;
    public int TargetY;
    public int BoundingWidth;
    public int BoundingHeight;
    public SKBitmap Image;
}

public enum SpriteType
{
    Sprite,
    Background,
    Font,
    Unknown
}


public enum SplitType
{
    Horizontal,
    Vertical,
}

public enum BestFitHeuristic
{
    Area,
    MaxOneAxis,
}

public struct Rect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class Node
{
    public Rect Bounds;
    public TextureInfo Texture;
    public SplitType SplitType;
}

public class Atlas
{
    public int Width;
    public int Height;
    public List<Node> Nodes;
}

static (int x, int y, int w, int h) GetBoundingBox(SKBitmap bmp)
{
    int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
    for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
            if (bmp.GetPixel(x, y).Alpha > 0)
            {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
    if (maxX < 0) return (0, 0, 0, 0);
    return (minX, minY, maxX - minX + 1, maxY - minY + 1);
}

static SKBitmap AddBorder(SKBitmap src)
{
    var bmp = new SKBitmap(src.Width + 2, src.Height + 2);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);
    canvas.DrawBitmap(src, 1, 1);
    return bmp;
}

static SKBitmap TrimBitmap(SKBitmap src)
{
    var (x, y, w, h) = GetBoundingBox(src);
    if (w == 0 || h == 0) return new SKBitmap(1, 1);
    var result = new SKBitmap(w, h);
    using var canvas = new SKCanvas(result);
    canvas.DrawBitmap(src, new SKRectI(x, y, x + w, y + h), new SKRectI(0, 0, w, h));
    return result;
}

public class Packer
{
    public List<TextureInfo> SourceTextures;
    public StringWriter Log;
    public StringWriter Error;
    public int Padding;
    public int AtlasSize;
    public bool DebugMode;
    public BestFitHeuristic FitHeuristic;
    public List<Atlas> Atlasses;
    public HashSet<string> Sources;

    public Packer()
    {
        SourceTextures = new List<TextureInfo>();
        Log = new StringWriter();
        Error = new StringWriter();
    }

    public void Process(string _SourceDir, int _AtlasSize, int _Padding, bool _DebugMode)
    {
        Padding = _Padding;
        AtlasSize = _AtlasSize;
        DebugMode = _DebugMode;
        //1: scan for all the textures we need to pack
        Sources = new HashSet<string>();
        ScanForTextures(_SourceDir);
        List<TextureInfo> textures = new List<TextureInfo>();
        textures = SourceTextures.ToList();
        //2: generate as many atlasses as needed (with the latest one as small as possible)
        Atlasses = new List<Atlas>();
        while (textures.Count > 0)
        {
            Atlas atlas = new Atlas();
            atlas.Width = _AtlasSize;
            atlas.Height = _AtlasSize;
            List<TextureInfo> leftovers = LayoutAtlas(textures, atlas);
            if (leftovers.Count == 0)
            {
                // we reached the last atlas. Check if this last atlas could have been twice smaller
                while (leftovers.Count == 0)
                {
                    atlas.Width /= 2;
                    atlas.Height /= 2;
                    leftovers = LayoutAtlas(textures, atlas);
                }
                // we need to go 1 step larger as we found the first size that is too small
                // if the atlas is 0x0 then it should be 1x1 instead
                if (atlas.Width == 0)
                {
                    atlas.Width = 1;
                } else
                {
                    atlas.Width *= 2;
                }
                if (atlas.Height == 0)
                {
                    atlas.Height = 1;
                }
                else
                {
                    atlas.Height *= 2;
                }
                leftovers = LayoutAtlas(textures, atlas);
            }
            Atlasses.Add(atlas);
            textures = leftovers;
        }
    }

    public void SaveAtlasses(string _Destination)
    {
        int atlasCount = 0;
        string prefix = _Destination.Replace(Path.GetExtension(_Destination), "");
        string descFile = _Destination;
        StreamWriter tw = new StreamWriter(_Destination);
        tw.WriteLine("source_tex, atlas_tex, x, y, width, height");
        foreach (Atlas atlas in Atlasses)
        {
            string atlasName = $"{prefix}{atlasCount:000}.png";

            // 1: Save images
            using (SKBitmap img = CreateAtlasImage(atlas))
                TextureWorkerSkia.SaveImageToFile(img, atlasName);

            // 2: save description in file
            foreach (Node n in atlas.Nodes)
            {
                if (n.Texture != null)
                {
                    tw.Write(n.Texture.Source + ", ");
                    tw.Write(atlasName + ", ");
                    tw.Write((n.Bounds.X).ToString() + ", ");
                    tw.Write((n.Bounds.Y).ToString() + ", ");
                    tw.Write((n.Bounds.Width).ToString() + ", ");
                    tw.WriteLine((n.Bounds.Height).ToString());
                }
            }
            ++atlasCount;
        }
        tw.Close();
        tw = new StreamWriter(prefix + ".log");
        tw.WriteLine("--- LOG -------------------------------------------");
        tw.WriteLine(Log.ToString());
        tw.WriteLine("--- ERROR -----------------------------------------");
        tw.WriteLine(Error.ToString());
        tw.Close();
    }

    private void ScanForTextures(string _Path)
    {
        DirectoryInfo di = new DirectoryInfo(_Path);
        FileInfo[] files = di.GetFiles("*", SearchOption.AllDirectories);
        foreach (FileInfo fi in files)
        {
            SpriteType spriteType = GetSpriteType(fi.FullName);
            string ext = Path.GetExtension(fi.FullName);

            bool isSprite = spriteType == SpriteType.Sprite || (spriteType == SpriteType.Unknown && importAsSprite);

            if (ext == ".gif")
            {
                // animated .gif - SkiaSharp does not support animated GIFs natively
                // Load first frame only and warn
                string dirName = Path.GetDirectoryName(fi.FullName);
                string spriteName = Path.GetFileNameWithoutExtension(fi.FullName);

                ScriptMessage("Warning: Animated GIF import is not supported with SkiaSharp. Only the first frame of '" + fi.Name + "' will be imported.");

                SKBitmap img = TextureWorkerSkia.ReadBGRAImageFromFile(fi.FullName);
                AddSource(
                    img,
                    Path.Join(
                        dirName,
                        isSprite ?
                            (spriteName + "_0.png") : (spriteName + ".png")
                    )
                );
            }
            else if (ext == ".png")
            {
                Match stripMatch = null;
                if (isSprite)
                {
                    stripMatch = Regex.Match(Path.GetFileNameWithoutExtension(fi.Name), @"(.*)_strip(\d+)");
                }
                if (stripMatch is not null && stripMatch.Success)
                {
                    string spriteName = stripMatch.Groups[1].Value;
                    string frameCountStr = stripMatch.Groups[2].Value;

                    uint frames;
                    try
                    {
                        frames = UInt32.Parse(frameCountStr);
                    }
                    catch
                    {
                        throw new ScriptException(fi.FullName + " has an invalid strip numbering scheme. Script has been stopped.");
                    }
                    if (frames <= 0)
                    {
                        throw new ScriptException(fi.FullName + " has 0 frames. Script has been stopped.");
                    }

                    if (!isSprite && frames > 1)
                    {
                        throw new ScriptException(fi.FullName + " is not a sprite, but has more than 1 frame. Script has been stopped.");
                    }

                    using SKBitmap img = TextureWorkerSkia.ReadBGRAImageFromFile(fi.FullName);
                    if ((img.Width % frames) > 0)
                    {
                        throw new ScriptException(fi.FullName + " has a width not divisible by the number of frames. Script has been stopped.");
                    }

                    string dirName = Path.GetDirectoryName(fi.FullName);

                    uint frameWidth = (uint)img.Width / frames;
                    uint frameHeight = (uint)img.Height;
                    for (uint i = 0; i < frames; i++)
                    {
                        var subset = new SKBitmap((int)frameWidth, (int)frameHeight);
                        using (var canvas = new SKCanvas(subset))
                        {
                            canvas.DrawBitmap(img,
                                new SKRectI((int)(frameWidth * i), 0, (int)(frameWidth * (i + 1)), (int)frameHeight),
                                new SKRectI(0, 0, (int)frameWidth, (int)frameHeight));
                        }
                        AddSource(
                            subset,
                            Path.Join(dirName,
                                isSprite ?
                                    (spriteName + "_" + i + ".png") : (spriteName + ".png")
                            )
                        );
                    }
                }
                else
                {
                    SKBitmap img = TextureWorkerSkia.ReadBGRAImageFromFile(fi.FullName);
                    AddSource(img, fi.FullName);
                }
            }
        }
    }

    private void AddSource(SKBitmap img, string fullName)
    {
        imagesToCleanup.Add(img);
        if (img.Width <= AtlasSize && img.Height <= AtlasSize)
        {
            TextureInfo ti = new TextureInfo();

            if (!Sources.Add(fullName))
            {
                throw new ScriptException(
                    Path.GetFileNameWithoutExtension(fullName) +
                    " as a frame already exists (possibly due to having multiple types of sprite images named the same). Script has been stopped."
                );
            }

            ti.Source = fullName;
            ti.BoundingWidth = (int)img.Width;
            ti.BoundingHeight = (int)img.Height;

            // GameMaker doesn't trim tilesets. I assume it didn't trim backgrounds too
            ti.TargetX = 0;
            ti.TargetY = 0;
            if (GetSpriteType(ti.Source) != SpriteType.Background)
            {
                // Add border, get bounding box, trim - equivalent to Magick.NET's Border/BoundingBox/Trim
                using var bordered = AddBorder(img);
                var bbox = GetBoundingBox(bordered);
                if (bbox.w > 0 && bbox.h > 0)
                {
                    ti.TargetX = bbox.x - 1;
                    ti.TargetY = bbox.y - 1;
                    // yes, TrimBitmap mutates the image conceptually...
                    // it doesn't really matter though since it isn't written back or anything
                    var trimmed = TrimBitmap(bordered);
                    // Replace img in cleanup list
                    imagesToCleanup.Remove(img);
                    img.Dispose();
                    img = trimmed;
                    imagesToCleanup.Add(img);
                }
                else
                {
                    // Empty sprites should be 1x1
                    ti.TargetX = 0;
                    ti.TargetY = 0;
                    var empty = new SKBitmap(1, 1);
                    imagesToCleanup.Remove(img);
                    img.Dispose();
                    img = empty;
                    imagesToCleanup.Add(img);
                }
            }
            ti.Width = (int)img.Width;
            ti.Height = (int)img.Height;
            ti.Image = img;

            SourceTextures.Add(ti);

            Log.WriteLine("Added " + fullName);
        }
        else
        {
            Error.WriteLine(fullName + " is too large to fix in the atlas. Skipping!");
        }
    }

    private void HorizontalSplit(Node _ToSplit, int _Width, int _Height, List<Node> _List)
    {
        Node n1 = new Node();
        n1.Bounds.X = _ToSplit.Bounds.X + _Width + Padding;
        n1.Bounds.Y = _ToSplit.Bounds.Y;
        n1.Bounds.Width = _ToSplit.Bounds.Width - _Width - Padding;
        n1.Bounds.Height = _Height;
        n1.SplitType = SplitType.Vertical;
        Node n2 = new Node();
        n2.Bounds.X = _ToSplit.Bounds.X;
        n2.Bounds.Y = _ToSplit.Bounds.Y + _Height + Padding;
        n2.Bounds.Width = _ToSplit.Bounds.Width;
        n2.Bounds.Height = _ToSplit.Bounds.Height - _Height - Padding;
        n2.SplitType = SplitType.Horizontal;
        if (n1.Bounds.Width > 0 && n1.Bounds.Height > 0)
            _List.Add(n1);
        if (n2.Bounds.Width > 0 && n2.Bounds.Height > 0)
            _List.Add(n2);
    }

    private void VerticalSplit(Node _ToSplit, int _Width, int _Height, List<Node> _List)
    {
        Node n1 = new Node();
        n1.Bounds.X = _ToSplit.Bounds.X + _Width + Padding;
        n1.Bounds.Y = _ToSplit.Bounds.Y;
        n1.Bounds.Width = _ToSplit.Bounds.Width - _Width - Padding;
        n1.Bounds.Height = _ToSplit.Bounds.Height;
        n1.SplitType = SplitType.Vertical;
        Node n2 = new Node();
        n2.Bounds.X = _ToSplit.Bounds.X;
        n2.Bounds.Y = _ToSplit.Bounds.Y + _Height + Padding;
        n2.Bounds.Width = _Width;
        n2.Bounds.Height = _ToSplit.Bounds.Height - _Height - Padding;
        n2.SplitType = SplitType.Horizontal;
        if (n1.Bounds.Width > 0 && n1.Bounds.Height > 0)
            _List.Add(n1);
        if (n2.Bounds.Width > 0 && n2.Bounds.Height > 0)
            _List.Add(n2);
    }

    private TextureInfo FindBestFitForNode(Node _Node, List<TextureInfo> _Textures)
    {
        TextureInfo bestFit = null;
        float nodeArea = _Node.Bounds.Width * _Node.Bounds.Height;
        float maxCriteria = 0.0f;
        foreach (TextureInfo ti in _Textures)
        {
            switch (FitHeuristic)
            {
                // Max of Width and Height ratios
                case BestFitHeuristic.MaxOneAxis:
                    if (ti.Width <= _Node.Bounds.Width && ti.Height <= _Node.Bounds.Height)
                    {
                        float wRatio = (float)ti.Width / (float)_Node.Bounds.Width;
                        float hRatio = (float)ti.Height / (float)_Node.Bounds.Height;
                        float ratio = wRatio > hRatio ? wRatio : hRatio;
                        if (ratio > maxCriteria)
                        {
                            maxCriteria = ratio;
                            bestFit = ti;
                        }
                    }
                    break;
                // Maximize Area coverage
                case BestFitHeuristic.Area:
                    if (ti.Width <= _Node.Bounds.Width && ti.Height <= _Node.Bounds.Height)
                    {
                        float textureArea = ti.Width * ti.Height;
                        float coverage = textureArea / nodeArea;
                        if (coverage > maxCriteria)
                        {
                            maxCriteria = coverage;
                            bestFit = ti;
                        }
                    }
                    break;
            }
        }
        return bestFit;
    }

    private List<TextureInfo> LayoutAtlas(List<TextureInfo> _Textures, Atlas _Atlas)
    {
        List<Node> freeList = new List<Node>();
        List<TextureInfo> textures = new List<TextureInfo>();
        _Atlas.Nodes = new List<Node>();
        textures = _Textures.ToList();
        Node root = new Node();
        root.Bounds.Width = _Atlas.Width;
        root.Bounds.Height = _Atlas.Height;
        root.SplitType = SplitType.Horizontal;
        freeList.Add(root);
        while (freeList.Count > 0 && textures.Count > 0)
        {
            Node node = freeList[0];
            freeList.RemoveAt(0);
            TextureInfo bestFit = FindBestFitForNode(node, textures);
            if (bestFit != null)
            {
                if (node.SplitType == SplitType.Horizontal)
                {
                    HorizontalSplit(node, bestFit.Width, bestFit.Height, freeList);
                }
                else
                {
                    VerticalSplit(node, bestFit.Width, bestFit.Height, freeList);
                }
                node.Texture = bestFit;
                node.Bounds.Width = bestFit.Width;
                node.Bounds.Height = bestFit.Height;
                textures.Remove(bestFit);
            }
            _Atlas.Nodes.Add(node);
        }
        return textures;
    }

    private SKBitmap CreateAtlasImage(Atlas _Atlas)
    {
        SKBitmap img = new SKBitmap(_Atlas.Width, _Atlas.Height);
        using var canvas = new SKCanvas(img);
        canvas.Clear(SKColors.Transparent);
        foreach (Node n in _Atlas.Nodes)
        {
            if (n.Texture is not null)
            {
                using SKBitmap resizedSourceImg = TextureWorkerSkia.ResizeImage(n.Texture.Image, n.Bounds.Width, n.Bounds.Height);
                canvas.DrawBitmap(resizedSourceImg, n.Bounds.X, n.Bounds.Y);
            }
        }
        return img;
    }
}

public static SpriteType GetSpriteType(string path)
{
    string folderPath = Path.GetDirectoryName(path);
    string folderName = new DirectoryInfo(folderPath).Name;
    string lowerName = folderName.ToLower();

    if (lowerName == "backgrounds" || lowerName == "background")
    {
        return SpriteType.Background;
    }
    else if (lowerName == "fonts" || lowerName == "font")
    {
        return SpriteType.Font;
    }
    else if (lowerName == "sprites" || lowerName == "sprite")
    {
        return SpriteType.Sprite;
    }
    return SpriteType.Unknown;
}

string CheckValidity()
{
    bool recursiveCheck = ScriptQuestion(@"This script imports all sprites in all subdirectories recursively.
If an image file is in a folder named ""Backgrounds"", then the image will be imported as a background.
Otherwise, the image will be imported as a sprite, and allow you to select its origin point and animation speed (if applicable).
Accepted sprite formats: separate frames starting at 0 or 1 (sprite_N.png), GM-style strip (sprite_stripN.png), animated GIF (sprite.gif - first frame only), optionally single image (sprite.png).
Accepted background formats: single image (bg.png), single-frame GIF (bg.gif).
Do you want to continue?");
    if (!recursiveCheck)
    {
        throw new ScriptCancelledException("Script cancelled.");
    }

    // Get import folder
    string importFolder = PromptChooseDirectory();
    if (importFolder is null)
    {
        throw new ScriptCancelledException("The import folder was not set.");
    }

    //Stop the script if there's missing sprite entries or w/e.
    bool hadMessage = false;
    bool hadFramelessMessage = false;
    string[] dirFiles = Directory.GetFiles(importFolder, "*.png", SearchOption.AllDirectories);
    foreach (string file in dirFiles)
    {
        string FileNameWithExtension = Path.GetFileName(file);
        string stripped = Path.GetFileNameWithoutExtension(file);
        int lastUnderscore = stripped.LastIndexOf('_');
        string spriteName = "";

        SpriteType spriteType = GetSpriteType(file);

        if ((spriteType != SpriteType.Sprite) && (spriteType != SpriteType.Background))
        {
            if (!hadMessage)
            {
                hadMessage = true;
                // this is annoying
                /*importAsSprite = ScriptQuestion(FileNameWithExtension + @" is in an incorrectly-named folder (valid names being ""Sprites"" and ""Backgrounds""). Would you like to import these images as sprites?
Pressing ""No"" will cause the program to ignore these images.");*/
                importAsSprite = true;
            }

            if (!importAsSprite)
            {
                continue;
            }
            else
            {
                spriteType = SpriteType.Sprite;
            }
        }

        // Check for duplicate filenames
        string[] dupFiles = Directory.GetFiles(importFolder, FileNameWithExtension, SearchOption.AllDirectories);
        if (dupFiles.Length > 1)
            throw new ScriptException("Duplicate file detected. There are " + dupFiles.Length + " files named: " + FileNameWithExtension);

        // Sprites can have multiple frames! Do some sprite-specific checking.
        if (spriteType == SpriteType.Sprite)
        {
            Match stripMatch = Regex.Match(stripped, @"(.*)_strip(\d+)");
            if (stripMatch.Success)
            {
                string frameCountStr = stripMatch.Groups[2].Value;

                int frames;
                try
                {
                    frames = Int32.Parse(frameCountStr);
                }
                catch
                {
                    throw new ScriptException(FileNameWithExtension + " has an invalid strip numbering scheme. Script has been stopped.");
                }
                if (frames <= 0)
                {
                    throw new ScriptException(FileNameWithExtension + " has 0 frames. Script has been stopped.");
                }

                // Probably a valid strip, can continue
                continue;
            }

            try
            {
                spriteName = stripped.Substring(0, lastUnderscore);

                // Check if the frame number is a valid string or not
                Int32.Parse(stripped.Substring(lastUnderscore + 1));
            }
            catch
            {
                if (!hadFramelessMessage)
                {
                    importFrameless = ScriptQuestion(FileNameWithExtension + @" does not seem to have a frame number or count. Import this image as a single-frame sprite named " + stripped + @"?
Pressing ""No"" will cause the program to ignore these images.");
                    hadFramelessMessage = true;
                }
                if (importFrameless)
                {
                    spriteName = stripped;
                }
                else
                {
                    continue;
                }
            }

            // If the sprite doesn't have an underscore, don't bother trying to parse it since it'll be single-frame anyways
            int frame = 0;
            if (spriteName != stripped)
            {
                Int32 validFrameNumber = 0;
                try
                {
                    validFrameNumber = Int32.Parse(stripped.Substring(lastUnderscore + 1));
                }
                catch
                {
                    if (!hadFramelessMessage)
                    {
                        importFrameless = ScriptQuestion(FileNameWithExtension + @" does not seem to have a frame number or count. Import this image as a single-frame sprite named " + stripped + @"?
	Pressing ""No"" will cause the program to ignore these images.");
                        hadFramelessMessage = true;
                    }
                    if (importFrameless)
                    {
                        spriteName = stripped;
                    }
                    else
                    {
                        continue;
                    }
                    // throw new ScriptException("The index of " + FileNameWithExtension + " could not be determined.");
                }
                try
                {
                    frame = Int32.Parse(stripped.Substring(lastUnderscore + 1));
                }
                catch
                {
                    throw new ScriptException(FileNameWithExtension + " is using letters instead of numbers. The script has stopped for your own protection.");
                }
            }

            int prevframe = 0;
            if (frame > 0)
            {
                prevframe = (frame - 1);
            }
            else if (frame < 0)
            {
                throw new ScriptException(spriteName + " is using an invalid numbering scheme. The script has stopped for your own protection.");
            }
            else
            {
                continue;
            }
            string prevFrameName = spriteName + "_" + prevframe.ToString() + ".png";
            string[] previousFrameFiles = Directory.GetFiles(importFolder, prevFrameName, SearchOption.AllDirectories);
            if (previousFrameFiles.Length < 1)
            {
                if (frame == 1)
                {
                    spritesStartAt1.Add(spriteName);
                }
                else
                {
                    throw new ScriptException(spriteName + " is missing one or more indexes. The detected missing index is: " + prevFrameName);
                }
            }
        }
    }
    return importFolder;
}

public void OffsetResult()
{
    // Origin position selection
    string offsetList = "Select origin position:\n";
    for (int i = 0; i < offsets.Length; i++)
    {
        offsetList += $"{i + 1}. {offsets[i]}\n";
    }
    string offsetInput = SimpleTextInput("Origin Position", offsetList, "1", false);
    if (!int.TryParse(offsetInput, out int offsetIndex) || offsetIndex < 1 || offsetIndex > offsets.Length)
    {
        offsetIndex = 1; // Default to Top Left
    }
    offresult = offsets[offsetIndex - 1];

    // Special type
    if (Data.IsGameMaker2())
    {
        string specialInput = SimpleTextInput("Special Type", "Is special type? (required for setting animation speed)\nEnter version number:", "1", false);
        if (!uint.TryParse(specialInput, out specialVer))
        {
            specialVer = 1;
        }
        isSpecial = true;

        // Animation speed
        string speedInput = SimpleTextInput("Animation Speed", "Enter animation speed:", "1", false);
        if (!float.TryParse(speedInput, out animSpd))
        {
            animSpd = 1;
        }

        // Playback type
        string playbackList = "Select playback type:\n";
        for (int i = 0; i < playbacks.Length; i++)
        {
            playbackList += $"{i + 1}. {playbacks[i]}\n";
        }
        string playbackInput = SimpleTextInput("Playback Type", playbackList, "2", false);
        if (!int.TryParse(playbackInput, out int playbackIndex) || playbackIndex < 1 || playbackIndex > playbacks.Length)
        {
            playbackIndex = 2; // Default to Frames Per Game Frame
        }
        playback = playbackIndex - 1;
    }
    else
    {
        isSpecial = false;
        specialVer = 1;
        animSpd = 1;
        playback = 0;
    }
}
