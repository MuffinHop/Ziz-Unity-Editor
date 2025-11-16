using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Reusable Texture Processing Utility for EMU Files
/// Handles texture optimization, format conversion, and Nintendo 64-style constraints
/// </summary>
public static class TextureProcessor
{
    /// <summary>
    /// Texture format options for hardware-constrained systems
    /// </summary>
    public enum OptimizedTextureFormat
    {
        RGBA16, // 44x44 max, 16-bit with 1-bit alpha
        RGBA32, // 32x32 max, 32-bit full RGBA
        CI8,    // 43x43 max, 256 colors with palette
        CI4,    // 64x64 max, 16 colors with palette
        Auto    // Automatically choose best format
    }

    /// <summary>
    /// Texture format information structure
    /// </summary>
    public struct TextureFormatInfo
    {
        public OptimizedTextureFormat format;
        public int size;
        public int maxTexels;
        public bool supportsPalette;
        public string description;
    }

    /// <summary>
    /// Process and optimize texture for hardware-constrained formats
    /// </summary>
    /// <param name="sourceTexture">Source texture to process</param>
    /// <param name="outputPath">Full path where optimized texture will be saved</param>
    /// <param name="targetFormat">Target format (or Auto for automatic selection)</param>
    /// <param name="maxSize">Maximum texture size override (0 = use format default)</param>
    /// <param name="enablePalettes">Allow palette-based formats (CI4/CI8)</param>
    /// <returns>Information about the processed texture format</returns>
    public static TextureFormatInfo ProcessAndOptimizeTexture(
        Texture2D sourceTexture, 
        string outputPath, 
        OptimizedTextureFormat targetFormat = OptimizedTextureFormat.Auto,
        int maxSize = 0,
        bool enablePalettes = true)
    {
        if (sourceTexture == null)
        {
            Debug.LogError("Source texture is null");
            return default(TextureFormatInfo);
        }

        try
        {
            Debug.Log($"<color=teal>Processing</color> texture: {Path.GetFileName(outputPath)}");

            // Determine optimal format and size
            var formatInfo = DetermineOptimalFormat(sourceTexture, targetFormat, maxSize, enablePalettes);

            // Create optimized texture
            Texture2D optimizedTexture = CreateOptimizedTexture(sourceTexture, formatInfo);

            // Save optimized texture
            SaveOptimizedTexture(optimizedTexture, outputPath, formatInfo);

            // Cleanup
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(optimizedTexture);
            else
                UnityEngine.Object.DestroyImmediate(optimizedTexture);

            Debug.Log($"<color=green>OK</color> Texture optimized: {Path.GetFileName(outputPath)} ({formatInfo.format}, {formatInfo.size}x{formatInfo.size})");
            return formatInfo;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Texture processing failed: {e.Message}");
            return default(TextureFormatInfo);
        }
    }

    /// <summary>
    /// Generate optimized filename based on format and size
    /// </summary>
    public static string GenerateOptimizedFilename(string originalFilename, TextureFormatInfo formatInfo)
    {
        string baseName = Path.GetFileNameWithoutExtension(originalFilename);
        string extension = ".png"; // Always save as PNG for compatibility

        return $"{baseName}_{formatInfo.format.ToString().ToLower()}_{formatInfo.size}x{formatInfo.size}{extension}";
    }

    /// <summary>
    /// Determine optimal texture format based on source texture and settings
    /// </summary>
    private static TextureFormatInfo DetermineOptimalFormat(
        Texture2D sourceTexture, 
        OptimizedTextureFormat targetFormat, 
        int maxSize, 
        bool enablePalettes)
    {
        var formatInfo = new TextureFormatInfo();

        if (targetFormat == OptimizedTextureFormat.Auto)
        {
            // Auto-determine based on texture characteristics
            bool hasAlpha = HasSignificantAlpha(sourceTexture);
            int colorCount = EstimateColorCount(sourceTexture);

            if (colorCount <= 16 && enablePalettes)
            {
                formatInfo.format = OptimizedTextureFormat.CI4;
                formatInfo.size = 64;
                formatInfo.maxTexels = 4096;
                formatInfo.supportsPalette = true;
                formatInfo.description = "CI4: 16 colors with palette";
            }
            else if (colorCount <= 256 && enablePalettes)
            {
                formatInfo.format = OptimizedTextureFormat.CI8;
                formatInfo.size = 43;
                formatInfo.maxTexels = 2048;
                formatInfo.supportsPalette = true;
                formatInfo.description = "CI8: 256 colors with palette";
            }
            else if (hasAlpha)
            {
                formatInfo.format = OptimizedTextureFormat.RGBA32;
                formatInfo.size = 32;
                formatInfo.maxTexels = 1024;
                formatInfo.supportsPalette = false;
                formatInfo.description = "RGBA32: Full 32-bit with alpha";
            }
            else
            {
                formatInfo.format = OptimizedTextureFormat.RGBA16;
                formatInfo.size = 44;
                formatInfo.maxTexels = 2048;
                formatInfo.supportsPalette = false;
                formatInfo.description = "RGBA16: 16-bit with 1-bit alpha";
            }
        }
        else
        {
            // Use specified format
            formatInfo.format = targetFormat;
            switch (targetFormat)
            {
                case OptimizedTextureFormat.RGBA16:
                    formatInfo.size = 44;
                    formatInfo.maxTexels = 2048;
                    formatInfo.supportsPalette = false;
                    formatInfo.description = "RGBA16: 16-bit with 1-bit alpha";
                    break;
                case OptimizedTextureFormat.RGBA32:
                    formatInfo.size = 32;
                    formatInfo.maxTexels = 1024;
                    formatInfo.supportsPalette = false;
                    formatInfo.description = "RGBA32: Full 32-bit with alpha";
                    break;
                case OptimizedTextureFormat.CI8:
                    formatInfo.size = 43;
                    formatInfo.maxTexels = 2048;
                    formatInfo.supportsPalette = true;
                    formatInfo.description = "CI8: 256 colors with palette";
                    break;
                case OptimizedTextureFormat.CI4:
                    formatInfo.size = 64;
                    formatInfo.maxTexels = 4096;
                    formatInfo.supportsPalette = true;
                    formatInfo.description = "CI4: 16 colors with palette";
                    break;
            }
        }

        // Apply user size override if specified
        if (maxSize > 0 && maxSize < formatInfo.size)
        {
            formatInfo.size = maxSize;
            formatInfo.maxTexels = maxSize * maxSize;
        }

        Debug.Log($"Selected format: {formatInfo.description}");
        return formatInfo;
    }

    /// <summary>
    /// Create optimized texture with specified format and size
    /// </summary>
    private static Texture2D CreateOptimizedTexture(Texture2D sourceTexture, TextureFormatInfo formatInfo)
    {
        // Make source texture readable
        Texture2D readableTexture = MakeTextureReadable(sourceTexture);

        // Resize if needed
        Texture2D resizedTexture = ResizeTexture(readableTexture, formatInfo.size, formatInfo.size);

        // Apply format-specific processing
        Texture2D optimizedTexture;
        switch (formatInfo.format)
        {
            case OptimizedTextureFormat.RGBA16:
                optimizedTexture = ConvertToRGBA16(resizedTexture);
                break;
            case OptimizedTextureFormat.RGBA32:
                optimizedTexture = ConvertToRGBA32(resizedTexture);
                break;
            case OptimizedTextureFormat.CI8:
                optimizedTexture = ConvertToCI8(resizedTexture);
                break;
            case OptimizedTextureFormat.CI4:
                optimizedTexture = ConvertToCI4(resizedTexture);
                break;
            default:
                optimizedTexture = resizedTexture;
                break;
        }

        // Cleanup intermediate textures
        if (resizedTexture != readableTexture && resizedTexture != optimizedTexture)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resizedTexture);
            else UnityEngine.Object.DestroyImmediate(resizedTexture);
        }

        if (readableTexture != sourceTexture && readableTexture != optimizedTexture)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(readableTexture);
            else UnityEngine.Object.DestroyImmediate(readableTexture);
        }

        return optimizedTexture;
    }

    /// <summary>
    /// Make texture readable by creating a copy
    /// </summary>
    private static Texture2D MakeTextureReadable(Texture2D sourceTexture)
    {
        if (sourceTexture.isReadable)
            return sourceTexture;

        // Create readable copy using RenderTexture
        RenderTexture renderTexture = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(sourceTexture, renderTexture);

        RenderTexture.active = renderTexture;
        Texture2D readableTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
        readableTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        readableTexture.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(renderTexture);

        return readableTexture;
    }

    /// <summary>
    /// Resize texture using bilinear filtering
    /// </summary>
    private static Texture2D ResizeTexture(Texture2D sourceTexture, int targetWidth, int targetHeight)
    {
        if (sourceTexture.width == targetWidth && sourceTexture.height == targetHeight)
            return sourceTexture;

        Color[] sourcePixels = sourceTexture.GetPixels();
        Color[] targetPixels = new Color[targetWidth * targetHeight];

        float xRatio = (float)sourceTexture.width / targetWidth;
        float yRatio = (float)sourceTexture.height / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                float sourceX = x * xRatio;
                float sourceY = y * yRatio;

                // Bilinear interpolation
                int x1 = Mathf.FloorToInt(sourceX);
                int y1 = Mathf.FloorToInt(sourceY);
                int x2 = Mathf.Min(x1 + 1, sourceTexture.width - 1);
                int y2 = Mathf.Min(y1 + 1, sourceTexture.height - 1);

                float xWeight = sourceX - x1;
                float yWeight = sourceY - y1;

                Color c1 = sourcePixels[y1 * sourceTexture.width + x1];
                Color c2 = sourcePixels[y1 * sourceTexture.width + x2];
                Color c3 = sourcePixels[y2 * sourceTexture.width + x1];
                Color c4 = sourcePixels[y2 * sourceTexture.width + x2];

                Color interpolated = Color.Lerp(
                    Color.Lerp(c1, c2, xWeight),
                    Color.Lerp(c3, c4, xWeight),
                    yWeight
                );

                targetPixels[y * targetWidth + x] = interpolated;
            }
        }

        Texture2D resizedTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        resizedTexture.SetPixels(targetPixels);
        resizedTexture.Apply();

        return resizedTexture;
    }

    /// <summary>
    /// Convert texture to RGBA16 format (5551)
    /// </summary>
    private static Texture2D ConvertToRGBA16(Texture2D sourceTexture)
    {
        Color[] pixels = sourceTexture.GetPixels();

        // Convert to 5551 format (5 bits R, 5 bits G, 5 bits B, 1 bit A)
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            
            // Quantize to 5 bits (0-31) for RGB and 1 bit for Alpha
            float r = Mathf.RoundToInt(pixel.r * 31f) / 31f;
            float g = Mathf.RoundToInt(pixel.g * 31f) / 31f;
            float b = Mathf.RoundToInt(pixel.b * 31f) / 31f;
            float a = pixel.a > 0.5f ? 1f : 0f;
            
            // Convert back to float
            pixels[i] = new Color(r, g, b, a);
        }

        Texture2D convertedTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
        convertedTexture.SetPixels(pixels);
        convertedTexture.Apply();

        return convertedTexture;
    }

    /// <summary>
    /// Convert texture to RGBA32 format (full quality)
    /// </summary>
    private static Texture2D ConvertToRGBA32(Texture2D sourceTexture)
    {
        // RGBA32 is already full quality, just ensure format
        Texture2D convertedTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
        convertedTexture.SetPixels(sourceTexture.GetPixels());
        convertedTexture.Apply();

        return convertedTexture;
    }

    /// <summary>
    /// Convert texture to CI8 format (256 color palette)
    /// </summary>
    private static Texture2D ConvertToCI8(Texture2D sourceTexture)
    {
        // Simple color quantization to 256 colors
        Color[] pixels = sourceTexture.GetPixels();
        Dictionary<Color, int> colorPalette = new Dictionary<Color, int>();
        List<Color> paletteColors = new List<Color>();

        // Build palette (simplified - just quantize each channel to reduce colors)
        foreach (Color pixel in pixels)
        {
            // Quantize to reduce color space
            Color quantized = new Color(
                Mathf.Round(pixel.r * 15) / 15f,
                Mathf.Round(pixel.g * 15) / 15f,
                Mathf.Round(pixel.b * 15) / 15f,
                Mathf.Round(pixel.a * 3) / 3f
            );

            if (!colorPalette.ContainsKey(quantized) && paletteColors.Count < 256)
            {
                colorPalette[quantized] = paletteColors.Count;
                paletteColors.Add(quantized);
            }
        }

        Debug.Log($"CI8 palette has {paletteColors.Count} colors");

        // Map pixels to palette
        for (int i = 0; i < pixels.Length; i++)
        {
            Color quantized = new Color(
                Mathf.Round(pixels[i].r * 15) / 15f,
                Mathf.Round(pixels[i].g * 15) / 15f,
                Mathf.Round(pixels[i].b * 15) / 15f,
                Mathf.Round(pixels[i].a * 3) / 3f
            );

            if (colorPalette.ContainsKey(quantized))
            {
                pixels[i] = paletteColors[colorPalette[quantized]];
            }
            else
            {
                // Find closest color in palette
                float minDistance = float.MaxValue;
                Color closestColor = paletteColors[0];

                foreach (Color paletteColor in paletteColors)
                {
                    float distance = Vector4.Distance(
                        new Vector4(quantized.r, quantized.g, quantized.b, quantized.a),
                        new Vector4(paletteColor.r, paletteColor.g, paletteColor.b, paletteColor.a)
                    );

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestColor = paletteColor;
                    }
                }

                pixels[i] = closestColor;
            }
        }

        Texture2D convertedTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
        convertedTexture.SetPixels(pixels);
        convertedTexture.Apply();

        return convertedTexture;
    }

    /// <summary>
    /// Convert texture to CI4 format (16 color palette)
    /// </summary>
    private static Texture2D ConvertToCI4(Texture2D sourceTexture)
    {
        // Simple color quantization to 16 colors
        Color[] pixels = sourceTexture.GetPixels();

        // Pre-defined 16-color palette (can be customized)
        Color[] palette = new Color[16]
        {
            Color.black, Color.white, Color.red, Color.green,
            Color.blue, Color.yellow, Color.cyan, Color.magenta,
            new Color(0.5f, 0.5f, 0.5f), new Color(0.25f, 0.25f, 0.25f),
            new Color(0.75f, 0.75f, 0.75f), new Color(1f, 0.5f, 0f),
            new Color(0.5f, 0f, 0.5f), new Color(0f, 0.5f, 0.5f),
            new Color(0.5f, 0.5f, 0f), new Color(0.75f, 0.25f, 0.25f)
        };

        // Map each pixel to closest palette color
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            float minDistance = float.MaxValue;
            Color closestColor = palette[0];

            foreach (Color paletteColor in palette)
            {
                float distance = Vector4.Distance(
                    new Vector4(pixel.r, pixel.g, pixel.b, pixel.a),
                    new Vector4(paletteColor.r, paletteColor.g, paletteColor.b, paletteColor.a)
                );

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestColor = paletteColor;
                }
            }

            pixels[i] = closestColor;
        }

        Texture2D convertedTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
        convertedTexture.SetPixels(pixels);
        convertedTexture.Apply();

        return convertedTexture;
    }

    /// <summary>
    /// Save optimized texture to file
    /// </summary>
    private static void SaveOptimizedTexture(Texture2D texture, string filePath, TextureFormatInfo formatInfo)
    {
        byte[] pngData = texture.EncodeToPNG();
        File.WriteAllBytes(filePath, pngData);

    Debug.Log($"<color=blue>Saved</color> optimized texture: {Path.GetFileName(filePath)} ({pngData.Length} bytes)");

        // Also save palette information for CI formats
        if (formatInfo.supportsPalette)
        {
            SavePaletteInfo(filePath, texture, formatInfo);
        }
    }

    /// <summary>
    /// Save palette information for CI formats
    /// </summary>
    private static void SavePaletteInfo(string texturePath, Texture2D texture, TextureFormatInfo formatInfo)
    {
        string paletteFilename = Path.ChangeExtension(texturePath, ".pal");

        // Extract unique colors as palette
        HashSet<Color> uniqueColors = new HashSet<Color>();
        Color[] pixels = texture.GetPixels();

        foreach (Color pixel in pixels)
        {
            uniqueColors.Add(pixel);
            if (uniqueColors.Count >= (formatInfo.format == OptimizedTextureFormat.CI4 ? 16 : 256))
                break;
        }

        // Save palette as simple text file
        using (StreamWriter writer = new StreamWriter(paletteFilename))
        {
            writer.WriteLine($"# Palette for {Path.GetFileName(texturePath)}");
            writer.WriteLine($"# Format: {formatInfo.format}");
            writer.WriteLine($"# Colors: {uniqueColors.Count}");
            writer.WriteLine("# Format: R G B A (0-255)");

            int index = 0;
            foreach (Color color in uniqueColors)
            {
                writer.WriteLine($"{index:D3}: {(int)(color.r * 255):D3} {(int)(color.g * 255):D3} {(int)(color.b * 255):D3} {(int)(color.a * 255):D3}");
                index++;
            }
        }

        Debug.Log($"📋 Saved palette: {Path.GetFileName(paletteFilename)} ({uniqueColors.Count} colors)");
    }

    /// <summary>
    /// Check if texture has significant alpha channel
    /// </summary>
    private static bool HasSignificantAlpha(Texture2D texture)
    {
        if (!texture.format.ToString().Contains("Alpha") && !texture.format.ToString().Contains("RGBA"))
            return false;

        // Sample some pixels to check for alpha variation
        Color[] pixels = texture.GetPixels();
        int sampleCount = Mathf.Min(100, pixels.Length);
        int alphaVariations = 0;

        for (int i = 0; i < sampleCount; i += sampleCount / 10)
        {
            if (pixels[i].a < 0.95f) alphaVariations++;
        }

        return alphaVariations > 2; // Has meaningful alpha if more than 2 samples have transparency
    }

    /// <summary>
    /// Estimate color count in texture (simplified)
    /// </summary>
    private static int EstimateColorCount(Texture2D texture)
    {
        // Simple estimation based on texture format and size
        if (texture.width * texture.height < 256)
            return 16; // Small textures likely have few colors
        else if (texture.width * texture.height < 1024)
            return 64;
        else
            return 1000; // Assume complex texture
    }

    /// <summary>
    /// Extract texture from GameObject's material
    /// </summary>
    public static Texture2D ExtractTextureFromGameObject(GameObject obj, bool includeChildren = true)
    {
        var renderer = obj.GetComponent<Renderer>();
        if (renderer?.material?.mainTexture is Texture2D texture)
        {
            return texture;
        }

        // Try to find texture in children
        if (includeChildren)
        {
            foreach (Transform child in obj.transform)
            {
                var childRenderer = child.GetComponent<Renderer>();
                if (childRenderer?.material?.mainTexture is Texture2D childTexture)
                {
                    return childTexture;
                }
            }
        }

        return null;
    }
}
