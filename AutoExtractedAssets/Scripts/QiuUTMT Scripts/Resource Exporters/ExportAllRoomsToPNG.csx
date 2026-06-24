using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using SkiaSharp;

EnsureDataLoaded();

string exportFolder = PromptChooseDirectory();
if (exportFolder is null)
    throw new ScriptCancelledException("The export folder was not set.");

SetProgressBar(null, "Rooms Exported", 0, Data.Rooms.Count);
StartProgressBarUpdater();

using TextureWorkerSkia worker = new TextureWorkerSkia();

foreach (UndertaleRoom room in Data.Rooms)
{
    if (room is null)
    {
        IncrementProgress();
        continue;
    }

    try
    {
        ExportRoom(room, worker, exportFolder);
    }
    catch (Exception e)
    {
        ScriptMessage($"Error exporting room \"{room.Name?.Content ?? "unknown"}\":\n{e.Message}");
    }

    IncrementProgress();
}

HideProgressBar();
ScriptMessage("Export complete.");

void ExportRoom(UndertaleRoom room, TextureWorkerSkia worker, string folder)
{
    int width = (int)room.Width;
    int height = (int)room.Height;
    if (width <= 0 || height <= 0)
        return;

    using var bmp = new SKBitmap(width, height);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Black);

    // Draw GMS1-style backgrounds (room.Backgrounds)
    foreach (var bg in room.Backgrounds)
    {
        if (bg is null || !bg.Enabled)
            continue;
        if (bg.BackgroundDefinition?.Texture is null)
            continue;

        try
        {
            using var bgBmp = worker.GetTextureFor(bg.BackgroundDefinition.Texture, bg.BackgroundDefinition.Name?.Content ?? "bg");
            if (bgBmp is null)
                continue;

            if (bg.Stretch)
            {
                canvas.DrawBitmap(bgBmp, new SKRect(0, 0, width, height));
            }
            else if (bg.TiledHorizontally || bg.TiledVertically)
            {
                float startX = bg.X;
                float startY = bg.Y;
                float drawW = bgBmp.Width;
                float drawH = bgBmp.Height;

                if (bg.TiledHorizontally && bg.TiledVertically)
                {
                    for (float y = startY; y < height; y += drawH)
                        for (float x = startX; x < width; x += drawW)
                            canvas.DrawBitmap(bgBmp, x, y);
                }
                else if (bg.TiledHorizontally)
                {
                    for (float x = startX; x < width; x += drawW)
                        canvas.DrawBitmap(bgBmp, x, startY);
                }
                else // TiledVertically only
                {
                    for (float y = startY; y < height; y += drawH)
                        canvas.DrawBitmap(bgBmp, startX, y);
                }
            }
            else
            {
                canvas.DrawBitmap(bgBmp, (float)bg.X, (float)bg.Y);
            }
        }
        catch { /* skip broken background textures */ }
    }

    // Draw GMS1-style tiles (room.Tiles), sorted by depth
    var sortedTiles = new List<UndertaleRoom.Tile>();
    foreach (var tile in room.Tiles)
    {
        if (tile is not null)
            sortedTiles.Add(tile);
    }
    sortedTiles.Sort((a, b) => a.TileDepth.CompareTo(b.TileDepth));

    foreach (var tile in sortedTiles)
    {
        DrawTile(tile, worker, canvas);
    }

    // Draw GMS2 layers (if present), sorted by depth
    if (room.Layers?.Count > 0)
    {
        var sortedLayers = new List<UndertaleRoom.Layer>();
        foreach (var layer in room.Layers)
        {
            if (layer is not null && layer.IsVisible)
                sortedLayers.Add(layer);
        }
        sortedLayers.Sort((a, b) => a.LayerDepth.CompareTo(b.LayerDepth));

        foreach (var layer in sortedLayers)
        {
            DrawLayer(layer, worker, canvas, width, height);
        }
    }

    // Draw GMS1-style game objects (room.GameObjects)
    foreach (var obj in room.GameObjects)
    {
        if (obj is null || obj.ObjectDefinition?.Sprite is null)
            continue;

        try
        {
            DrawGameObjectSprite(obj.ObjectDefinition.Sprite, obj.X, obj.Y, obj.ScaleX, obj.ScaleY, obj.ImageIndex, worker, canvas);
        }
        catch { /* skip broken objects */ }
    }

    // Save
    string path = Path.Combine(folder, room.Name.Content + ".png");
    TextureWorkerSkia.SaveImageToFile(bmp, path);
}

void DrawTile(UndertaleRoom.Tile tile, TextureWorkerSkia worker, SKCanvas canvas)
{
    try
    {
        UndertaleTexturePageItem texItem = tile.spriteMode
            ? tile.SpriteDefinition?.Textures?.Count > 0
                ? tile.SpriteDefinition.Textures[0]?.Texture
                : null
            : tile.BackgroundDefinition?.Texture;

        if (texItem is null)
            return;

        using var tileBmp = worker.GetTextureFor(texItem, tile.spriteMode
            ? tile.SpriteDefinition?.Name?.Content ?? "tile"
            : tile.BackgroundDefinition?.Name?.Content ?? "tile");
        if (tileBmp is null)
            return;

        float srcX = tile.SourceX;
        float srcY = tile.SourceY;
        float srcW = tile.Width;
        float srcH = tile.Height;

        // Clamp source rect to bitmap bounds
        srcW = Math.Min(srcW, tileBmp.Width - srcX);
        srcH = Math.Min(srcH, tileBmp.Height - srcY);
        if (srcW <= 0 || srcH <= 0)
            return;

        var srcRect = new SKRect(srcX, srcY, srcX + srcW, srcY + srcH);
        var dstRect = new SKRect(tile.X, tile.Y,
                                 tile.X + srcW * tile.ScaleX,
                                 tile.Y + srcH * tile.ScaleY);

        canvas.DrawBitmap(tileBmp, srcRect, dstRect);
    }
    catch { /* skip broken tiles */ }
}

void DrawLayer(UndertaleRoom.Layer layer, TextureWorkerSkia worker, SKCanvas canvas, int roomWidth, int roomHeight)
{
    try
    {
        switch (layer.LayerType)
        {
            case UndertaleRoom.LayerType.Background:
                DrawLayerBackground(layer, worker, canvas, roomWidth, roomHeight);
                break;
            case UndertaleRoom.LayerType.Instances:
                DrawLayerInstances(layer, worker, canvas);
                break;
            case UndertaleRoom.LayerType.Assets:
                DrawLayerAssets(layer, worker, canvas);
                break;
            case UndertaleRoom.LayerType.Tiles:
                DrawLayerTiles(layer, worker, canvas);
                break;
        }
    }
    catch { /* skip broken layers */ }
}

void DrawLayerBackground(UndertaleRoom.Layer layer, TextureWorkerSkia worker, SKCanvas canvas, int roomWidth, int roomHeight)
{
    var bgData = layer.BackgroundData;
    if (bgData is null || !bgData.Visible || bgData.Sprite is null)
        return;

    if (bgData.Sprite.Textures.Count == 0)
        return;

    int frame = Math.Min((int)bgData.FirstFrame, bgData.Sprite.Textures.Count - 1);
    if (frame < 0) frame = 0;

    var texEntry = bgData.Sprite.Textures[frame];
    if (texEntry?.Texture is null)
        return;

    try
    {
        using var spriteBmp = worker.GetTextureFor(texEntry.Texture, bgData.Sprite.Name?.Content ?? "bg");
        if (spriteBmp is null)
            return;

        float offsetX = layer.XOffset;
        float offsetY = layer.YOffset;

        if (bgData.Stretch)
        {
            var dstRect = new SKRect(offsetX, offsetY, offsetX + roomWidth, offsetY + roomHeight);
            canvas.DrawBitmap(spriteBmp, dstRect);
        }
        else if (bgData.TiledHorizontally || bgData.TiledVertically)
        {
            float drawW = spriteBmp.Width;
            float drawH = spriteBmp.Height;

            if (bgData.TiledHorizontally && bgData.TiledVertically)
            {
                for (float y = offsetY; y < roomHeight; y += drawH)
                    for (float x = offsetX; x < roomWidth; x += drawW)
                        canvas.DrawBitmap(spriteBmp, x, y);
            }
            else if (bgData.TiledHorizontally)
            {
                for (float x = offsetX; x < roomWidth; x += drawW)
                    canvas.DrawBitmap(spriteBmp, x, offsetY);
            }
            else
            {
                for (float y = offsetY; y < roomHeight; y += drawH)
                    canvas.DrawBitmap(spriteBmp, offsetX, y);
            }
        }
        else
        {
            canvas.DrawBitmap(spriteBmp, offsetX, offsetY);
        }
    }
    catch { /* skip broken background layer */ }
}

void DrawLayerInstances(UndertaleRoom.Layer layer, TextureWorkerSkia worker, SKCanvas canvas)
{
    var instancesData = layer.InstancesData;
    if (instancesData?.Instances is null)
        return;

    foreach (var obj in instancesData.Instances)
    {
        if (obj is null || obj.ObjectDefinition?.Sprite is null)
            continue;

        try
        {
            DrawGameObjectSprite(obj.ObjectDefinition.Sprite, obj.X, obj.Y, obj.ScaleX, obj.ScaleY, obj.ImageIndex, worker, canvas);
        }
        catch { /* skip broken instances */ }
    }
}

void DrawLayerAssets(UndertaleRoom.Layer layer, TextureWorkerSkia worker, SKCanvas canvas)
{
    var assetsData = layer.AssetsData;
    if (assetsData is null)
        return;

    // Draw legacy tiles from assets layer
    if (assetsData.LegacyTiles?.Count > 0)
    {
        foreach (var tile in assetsData.LegacyTiles)
        {
            if (tile is not null)
                DrawTile(tile, worker, canvas);
        }
    }

    // Draw sprite instances
    if (assetsData.Sprites?.Count > 0)
    {
        foreach (var spriteInst in assetsData.Sprites)
        {
            if (spriteInst is null || spriteInst.Sprite is null)
                continue;

            try
            {
                int frame = spriteInst.WrappedFrameIndex;
                DrawSpriteAt(spriteInst.Sprite, spriteInst.X, spriteInst.Y,
                             spriteInst.ScaleX, spriteInst.ScaleY, frame, worker, canvas);
            }
            catch { /* skip broken sprite instances */ }
        }
    }
}

void DrawLayerTiles(UndertaleRoom.Layer layer, TextureWorkerSkia worker, SKCanvas canvas)
{
    var tilesData = layer.TilesData;
    if (tilesData is null || tilesData.Background is null || tilesData.TileData is null)
        return;

    UndertaleBackground tileset = tilesData.Background;
    if (tileset.Texture is null)
        return;

    // For GMS2 tile layers, we need to render individual tiles from the tileset
    uint tileW = tileset.GMS2TileWidth;
    uint tileH = tileset.GMS2TileHeight;
    uint outputBorderX = tileset.GMS2OutputBorderX;
    uint outputBorderY = tileset.GMS2OutputBorderY;
    uint cols = tileset.GMS2TileColumns;
    float layerXOffset = layer.XOffset;
    float layerYOffset = layer.YOffset;

    try
    {
        using var tilesetBmp = worker.GetTextureFor(tileset.Texture, tileset.Name?.Content ?? "tileset");
        if (tilesetBmp is null)
            return;

        for (uint ty = 0; ty < tilesData.TilesY; ty++)
        {
            if (ty >= tilesData.TileData.Length)
                break;
            for (uint tx = 0; tx < tilesData.TilesX; tx++)
            {
                if (tx >= tilesData.TileData[ty].Length)
                    break;

                uint tileId = tilesData.TileData[ty][tx];
                if (tileId == 0xFFFFFFFF) // empty tile
                    continue;

                // Decode tile ID: bits 0-15 = actual tile index in tileset, higher bits are flags
                uint actualIndex = tileId & 0x0FFFFFFF;
                if (actualIndex == 0)
                    continue;

                // Tile indices are 1-based in GMS2
                actualIndex -= 1;

                // Calculate source position in the tileset texture
                uint srcCol = actualIndex % cols;
                uint srcRow = actualIndex / cols;

                float srcX = outputBorderX + srcCol * (tileW + outputBorderX * 2);
                float srcY = outputBorderY + srcRow * (tileH + outputBorderY * 2);

                // Clamp source rect
                if (srcX + tileW > tilesetBmp.Width || srcY + tileH > tilesetBmp.Height)
                    continue;

                float dstX = layerXOffset + tx * tileW;
                float dstY = layerYOffset + ty * tileH;

                var srcRect = new SKRect(srcX, srcY, srcX + tileW, srcY + tileH);
                var dstRect = new SKRect(dstX, dstY, dstX + tileW, dstY + tileH);

                canvas.DrawBitmap(tilesetBmp, srcRect, dstRect);
            }
        }
    }
    catch { /* skip broken tile layer */ }
}

void DrawGameObjectSprite(UndertaleSprite sprite, int x, int y, float scaleX, float scaleY, int imageIndex, TextureWorkerSkia worker, SKCanvas canvas)
{
    if (sprite.Textures?.Count == 0)
        return;

    int frame = imageIndex;
    if (frame < 0) frame = 0;
    if (frame >= sprite.Textures.Count)
        frame = ((frame % sprite.Textures.Count) + sprite.Textures.Count) % sprite.Textures.Count;

    DrawSpriteAt(sprite, x, y, scaleX, scaleY, frame, worker, canvas);
}

void DrawSpriteAt(UndertaleSprite sprite, int x, int y, float scaleX, float scaleY, int frame, TextureWorkerSkia worker, SKCanvas canvas)
{
    if (sprite.Textures?.Count == 0 || frame < 0 || frame >= sprite.Textures.Count)
        return;

    var texEntry = sprite.Textures[frame];
    if (texEntry?.Texture is null)
        return;

    try
    {
        using var spriteBmp = worker.GetTextureFor(texEntry.Texture, sprite.Name?.Content ?? "sprite");
        if (spriteBmp is null)
            return;

        float drawX = x - sprite.OriginX + texEntry.Texture.TargetX;
        float drawY = y - sprite.OriginY + texEntry.Texture.TargetY;

        if (Math.Abs(scaleX - 1f) < 0.001f && Math.Abs(scaleY - 1f) < 0.001f)
        {
            canvas.DrawBitmap(spriteBmp, drawX, drawY);
        }
        else
        {
            var dstRect = new SKRect(drawX, drawY,
                                     drawX + spriteBmp.Width * scaleX,
                                     drawY + spriteBmp.Height * scaleY);
            canvas.DrawBitmap(spriteBmp, dstRect);
        }
    }
    catch { /* skip broken sprite */ }
}
