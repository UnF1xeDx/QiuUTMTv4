using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UndertaleModLib.Util;
using SkiaSharp;

string importFolderA = PromptChooseDirectory();
if (importFolderA is null) 
    throw new ScriptCancelledException("The import folder was not set.");

string importFolderB = PromptChooseDirectory();
if (importFolderB is null)
    throw new ScriptCancelledException("The import folder was not set.");

string exportFolder = PromptChooseDirectory();
if (exportFolder is null) 
    throw new ScriptCancelledException("The export folder was not set.");

// Loop over all PNG files in folder A
DirectoryInfo textureDirectoryA = new DirectoryInfo(importFolderA);
FileInfo[] filesA = textureDirectoryA.GetFiles("*.png", SearchOption.AllDirectories);
foreach (FileInfo fileA in filesA) 
{
    // If there's no matching file found, abort
    if (!File.Exists(Paths.JoinVerifyWithinDirectory(importFolderB, fileA.Name)))
        continue;
    
    // Load both images, and calculate dimensions of resulting image
    using SKBitmap imageA = TextureWorkerSkia.ReadBGRAImageFromFile(Paths.JoinVerifyWithinDirectory(importFolderA, fileA.Name));
    using SKBitmap imageB = TextureWorkerSkia.ReadBGRAImageFromFile(Paths.JoinVerifyWithinDirectory(importFolderB, fileA.Name));
    int width = imageA.Width + imageB.Width;
    int height = Math.Max(imageA.Height, imageB.Height);

    // Make combined image, and composite both images onto it
    using SKBitmap outputImage = new(width, height);
    using (SKCanvas canvas = new(outputImage))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(imageA, 0, 0);
        canvas.DrawBitmap(imageB, imageA.Width, 0);
    }

    // Save image to output folder
    TextureWorkerSkia.SaveImageToFile(outputImage, Paths.JoinVerifyWithinDirectory(exportFolder, fileA.Name));
}
