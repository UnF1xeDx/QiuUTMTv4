/*
    Exports sprites as individual PNG frames in a subfolder per sprite.
    Script made by CST1229, with parts based off of ExportAllSprites.csx.

    Originally ExportSpritesAsGIF.csx using Magick.NET GIF support.
    Rewritten to use SkiaSharp, exporting frames as PNGs + info.txt instead.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using UndertaleModLib.Scripting;
using SkiaSharp;

EnsureDataLoaded();

string folder = PromptChooseDirectory();
if (folder is null)
{
    return;
}

string filter = SimpleTextInput("Filter sprites", "String that the sprite names must start with (or leave blank to export all):", "", false);
await ExtractSprites(folder, filter);

async Task ExtractSprites(string folder, string prefix)
{
    using TextureWorkerSkia worker = new TextureWorkerSkia();
    IList<UndertaleSprite> sprites = Data.Sprites;
    if (prefix != "")
    {
        sprites = new List<UndertaleSprite> { };
        foreach (UndertaleSprite sprite in Data.Sprites)
        {
            if (sprite.Name.Content.StartsWith(prefix))
            {
                sprites.Add(sprite);
            }
        }
    }

    SetProgressBar(null, "Exporting sprites as PNG frames...", 0, sprites.Count);
    StartProgressBarUpdater();

    bool isParallel = true;
    await Task.Run(() =>
    {
        if (isParallel)
        {
            Parallel.ForEach(sprites, (sprite) =>
            {
                IncrementProgressParallel();
                ExtractSprite(sprite, folder, worker);
            });
        }
        else
        {
            foreach (UndertaleSprite sprite in sprites)
            {
                ExtractSprite(sprite, folder, worker);
                IncrementProgressParallel();
            }
        }
    });
    await StopProgressBarUpdater();
    HideProgressBar();
}

void ExtractSprite(UndertaleSprite sprite, string folder, TextureWorkerSkia worker)
{
    string spriteFolder = Path.Join(folder, sprite.Name.Content);
    var frameDelays = new List<int>();
    bool anyValidFrames = false;

    for (int picCount = 0; picCount < sprite.Textures.Count; picCount++)
    {
        if (sprite.Textures[picCount]?.Texture != null)
        {
            using SKBitmap image = worker.GetTextureFor(sprite.Textures[picCount].Texture, sprite.Name.Content + " (frame " + picCount + ")", true);

            if (!anyValidFrames)
            {
                Directory.CreateDirectory(spriteFolder);
            }

            string framePath = Path.Join(spriteFolder, "frame_" + picCount + ".png");
            TextureWorkerSkia.SaveImageToFile(image, framePath);

            // Calculate animation delay (in centiseconds, same unit as GIF)
            int delay;
            if (sprite.IsSpecialType && Data.IsGameMaker2())
            {
                if (sprite.GMS2PlaybackSpeed == 0f)
                {
                    delay = 10;
                }
                else if (sprite.GMS2PlaybackSpeedType is AnimSpeedType.FramesPerGameFrame)
                {
                    delay = Math.Max((int)(Math.Round(100f / (sprite.GMS2PlaybackSpeed * Data.GeneralInfo.GMS2FPS))), 1);
                }
                else
                {
                    delay = Math.Max((int)(Math.Round(100 / sprite.GMS2PlaybackSpeed)), 1);
                }
            }
            else
            {
                delay = 3; // 30fps
            }
            frameDelays.Add(delay);
            anyValidFrames = true;
        }
    }

    if (!anyValidFrames)
    {
        return;
    }

    // Write animation timing info
    string infoPath = Path.Join(spriteFolder, "info.txt");
    var sb = new StringBuilder();
    sb.AppendLine("sprite=" + sprite.Name.Content);
    sb.AppendLine("frames=" + frameDelays.Count);
    for (int i = 0; i < frameDelays.Count; i++)
    {
        sb.AppendLine("frame_" + i + "_delay_cs=" + frameDelays[i]);
    }
    File.WriteAllText(infoPath, sb.ToString());
}
