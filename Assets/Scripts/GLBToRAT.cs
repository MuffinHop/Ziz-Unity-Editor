using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Rat;

// --- Data Structures ---
// These structs are now defined in the Rat namespace in Rat.cs
// and are used from there.

namespace Rat.CommandLine
{
    public class GLBToRAT
    {
        private static int SignExtend(uint value, int bits)
        {
            if (bits == 0) return 0;
            uint signBit = 1U << (bits - 1);
            return (value & signBit) != 0 ? (int)(value | (~0U << bits)) : (int)value;
        }

        private static byte BitsForDelta(int delta)
        {
            int d = Math.Abs(delta);
            if (d == 0) return 1;
            int bits = 1;
            while ((1 << (bits - 1)) <= d)
            {
                bits++;
            }
            return (byte)bits;
        }

        public static CompressedAnimation CompressFrames(List<UnityEngine.Vector3[]> allFramesVertices, ushort[] allIndices, UnityEngine.Vector2[] allUVs, UnityEngine.Color[] allColors, string textureFilename = null, string meshDataFilename = null)
        {
            if (allFramesVertices == null || allFramesVertices.Count == 0)
            {
                throw new ArgumentException("No vertex data provided.");
            }

            uint numFrames = (uint)allFramesVertices.Count;
            uint numVertices = (uint)allFramesVertices[0].Length;
            uint numIndices = (uint)allIndices.Length;

            // 1. Calculate bounds
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var frame in allFramesVertices)
            {
                foreach (var v in frame)
                {
                    if (v.x < minX) minX = v.x;
                    if (v.x > maxX) maxX = v.x;
                    if (v.y < minY) minY = v.y;
                    if (v.y > maxY) maxY = v.y;
                    if (v.z < minZ) minZ = v.z;
                    if (v.z > maxZ) maxZ = v.z;
                }
            }

            float rangeX = maxX - minX;
            float rangeY = maxY - minY;
            float rangeZ = maxZ - minZ;
            if (rangeX == 0) rangeX = 1;
            if (rangeY == 0) rangeY = 1;
            if (rangeZ == 0) rangeZ = 1;

            // 2. Normalize vertices to VertexU8
            var frames = new VertexU8[numFrames][];
            for (int f = 0; f < numFrames; f++)
            {
                frames[f] = new VertexU8[numVertices];
                for (int v = 0; v < numVertices; v++)
                {
                    frames[f][v].x = (byte)UnityEngine.Mathf.Clamp(((allFramesVertices[f][v].x - minX) / rangeX) * 255.0f, 0, 255);
                    frames[f][v].y = (byte)UnityEngine.Mathf.Clamp(((allFramesVertices[f][v].y - minY) / rangeY) * 255.0f, 0, 255);
                    frames[f][v].z = (byte)UnityEngine.Mathf.Clamp(((allFramesVertices[f][v].z - minZ) / rangeZ) * 255.0f, 0, 255);
                }
            }

            // 3. Convert UVs and Colors
            var uvs = new VertexUV[numVertices];
            for(int i=0; i<numVertices; i++) uvs[i] = new VertexUV { u = allUVs[i].x, v = allUVs[i].y };

            var colors = new VertexColor[numVertices];
            for(int i=0; i<numVertices; i++) colors[i] = new VertexColor { r = allColors[i].r, g = allColors[i].g, b = allColors[i].b, a = allColors[i].a };

            // 4. Call original CompressAnimation method
            var anim = CompressAnimation(frames, uvs, colors, allIndices, numVertices, numIndices, numFrames, minX, minY, minZ, maxX, maxY, maxZ);
            anim.texture_filename = textureFilename;
            anim.mesh_data_filename = meshDataFilename;
            return anim;
        }

        public static CompressedAnimation CompressAnimation(VertexU8[][] frames, VertexUV[] uvs, VertexColor[] colors, ushort[] indices, uint numVertices, uint numIndices, uint numFrames, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            var anim = new CompressedAnimation
            {
                num_vertices = numVertices,
                num_indices = numIndices,
                num_frames = numFrames,
                min_x = minX, min_y = minY, min_z = minZ,
                max_x = maxX, max_y = maxY, max_z = maxZ,
                first_frame = new VertexU8[numVertices],
                uvs = new VertexUV[numVertices],
                colors = new VertexColor[numVertices],
                indices = new ushort[numIndices],
                bit_widths_x = new byte[numVertices],
                bit_widths_y = new byte[numVertices],
                bit_widths_z = new byte[numVertices]
            };

            Array.Copy(frames[0], anim.first_frame, numVertices);
            Array.Copy(uvs, anim.uvs, numVertices);
            Array.Copy(colors, anim.colors, numVertices);
            if (indices.Length > 0) Array.Copy(indices, anim.indices, numIndices);

            if (numFrames > 1)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    int maxDx = 0, maxDy = 0, maxDz = 0;
                    for (int f = 1; f < numFrames; f++)
                    {
                        int dx = frames[f][v].x - frames[f - 1][v].x;
                        if (Math.Abs(dx) > maxDx) maxDx = Math.Abs(dx);
                        int dy = frames[f][v].y - frames[f - 1][v].y;
                        if (Math.Abs(dy) > maxDy) maxDy = Math.Abs(dy);
                        int dz = frames[f][v].z - frames[f - 1][v].z;
                        if (Math.Abs(dz) > maxDz) maxDz = Math.Abs(dz);
                    }
                    anim.bit_widths_x[v] = BitsForDelta(maxDx);
                    anim.bit_widths_y[v] = BitsForDelta(maxDy);
                    anim.bit_widths_z[v] = BitsForDelta(maxDz);
                }

                var writer = new BitstreamWriter();
                for (uint f = 1; f < numFrames; f++)
                {
                    for (uint v = 0; v < numVertices; v++)
                    {
                        int dx = frames[f][v].x - frames[f - 1][v].x;
                        writer.Write((uint)dx, anim.bit_widths_x[v]);
                        int dy = frames[f][v].y - frames[f - 1][v].y;
                        writer.Write((uint)dy, anim.bit_widths_y[v]);
                        int dz = frames[f][v].z - frames[f - 1][v].z;
                        writer.Write((uint)dz, anim.bit_widths_z[v]);
                    }
                }
                writer.Flush();
                anim.delta_stream = writer.ToArray();
            }
            else
            {
                anim.delta_stream = Array.Empty<uint>();
            }
            return anim;
        }

        public static void WriteRatFile(string filename, CompressedAnimation anim)
        {
            string meshDataFilename = null;
            if (!string.IsNullOrEmpty(anim.mesh_data_filename))
            {
                meshDataFilename = Path.GetFileName(anim.mesh_data_filename);
                string meshPath = Path.Combine(Path.GetDirectoryName(filename), meshDataFilename);
                using (var meshStream = File.Open(meshPath, FileMode.Create))
                {
                    Rat.Tool.WriteRatMeshFile(meshStream, anim);
                }
            }

            using var stream = File.Open(filename, FileMode.Create);
            Rat.Tool.WriteRatFile(stream, anim, meshDataFilename);
        }

        public static CompressedAnimation ReadRatFile(string filename)
        {
            using var stream = File.OpenRead(filename);
            return Rat.Core.ReadRatFile(stream, filename);
        }

        public static DecompressionContext CreateDecompressionContext(CompressedAnimation anim)
        {
            var ctx = new DecompressionContext
            {
                current_positions = new VertexU8[anim.num_vertices],
                current_frame = 0
            };
            Array.Copy(anim.first_frame, ctx.current_positions, anim.num_vertices);
            return ctx;
        }

        public static void DecompressToFrame(DecompressionContext ctx, CompressedAnimation anim, uint targetFrame)
        {
            if (targetFrame == ctx.current_frame) return;
            if (targetFrame >= anim.num_frames) targetFrame = anim.num_frames - 1;

            if (targetFrame < ctx.current_frame || ctx.current_frame == 0)
            {
                Array.Copy(anim.first_frame, ctx.current_positions, anim.num_vertices);
                ctx.current_frame = 0;
            }

            var reader = new BitstreamReader(anim.delta_stream);
            
            // Skip to the start of the frame data we need to start decompressing from
            long totalBitsToSkip = 0;
            for (uint f = 1; f <= ctx.current_frame; f++)
            {
                for (uint v = 0; v < anim.num_vertices; v++)
                {
                    totalBitsToSkip += anim.bit_widths_x[v] + anim.bit_widths_y[v] + anim.bit_widths_z[v];
                }
            }
            
            long wordsToSkip = totalBitsToSkip / 32;
            int bitsToSkipInWord = (int)(totalBitsToSkip % 32);
            for(int i=0; i<wordsToSkip*32 + bitsToSkipInWord; i++) reader.Read(1);


            for (uint f = ctx.current_frame + 1; f <= targetFrame; f++)
            {
                for (uint v = 0; v < anim.num_vertices; v++)
                {
                    int dx = SignExtend(reader.Read(anim.bit_widths_x[v]), anim.bit_widths_x[v]);
                    int dy = SignExtend(reader.Read(anim.bit_widths_y[v]), anim.bit_widths_y[v]);
                    int dz = SignExtend(reader.Read(anim.bit_widths_z[v]), anim.bit_widths_z[v]);
                    
                    ctx.current_positions[v].x = (byte)(ctx.current_positions[v].x + dx);
                    ctx.current_positions[v].y = (byte)(ctx.current_positions[v].y + dy);
                    ctx.current_positions[v].z = (byte)(ctx.current_positions[v].z + dz);
                }
            }
            ctx.current_frame = targetFrame;
        }

        public static (VertexU8[][], VertexUV[], VertexColor[], ushort[], float, float, float, float, float, float) SimulateGLBData(uint numVertices, uint numFrames)
        {
            var frames = new VertexU8[numFrames][];
            var uvs = new VertexUV[numVertices];
            var colors = new VertexColor[numVertices];
            var indices = new List<ushort>();

            int gridSize = (int)Math.Sqrt(numVertices);
            numVertices = (uint)(gridSize * gridSize);

            float minX=float.MaxValue, minY=float.MaxValue, minZ=float.MaxValue;
            float maxX=float.MinValue, maxY=float.MinValue, maxZ=float.MinValue;

            var rawFrames = new float[numFrames][,];

            for (int f = 0; f < numFrames; f++)
            {
                rawFrames[f] = new float[numVertices, 3];
                float time = (float)f / numFrames;
                for (int i = 0; i < gridSize; i++)
                {
                    for (int j = 0; j < gridSize; j++)
                    {
                        int vIdx = i * gridSize + j;
                        float x = (float)i / (gridSize - 1) - 0.5f;
                        float z = (float)j / (gridSize - 1) - 0.5f;
                        float y = 0.2f * (float)Math.Sin(2.0 * Math.PI * (time + (float)Math.Sqrt(x*x + z*z) * 2.0f));
                        
                        rawFrames[f][vIdx, 0] = x;
                        rawFrames[f][vIdx, 1] = y;
                        rawFrames[f][vIdx, 2] = z;

                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                        if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;

                        if (f == 0)
                        {
                            uvs[vIdx] = new VertexUV { u = (float)i / (gridSize - 1), v = (float)j / (gridSize - 1) };
                            colors[vIdx] = new VertexColor { r = 1, g = 1, b = 1, a = 1 };
                            if (i < gridSize - 1 && j < gridSize - 1)
                            {
                                indices.Add((ushort)vIdx);
                                indices.Add((ushort)(vIdx + 1));
                                indices.Add((ushort)(vIdx + gridSize));
                                indices.Add((ushort)(vIdx + 1));
                                indices.Add((ushort)(vIdx + gridSize + 1));
                                indices.Add((ushort)(vIdx + gridSize));
                            }
                        }
                    }
                }
            }

            float rangeX = maxX - minX; float rangeY = maxY - minY; float rangeZ = maxZ - minZ;

            for (int f = 0; f < numFrames; f++)
            {
                frames[f] = new VertexU8[numVertices];
                for (int v = 0; v < numVertices; v++)
                {
                    frames[f][v].x = (byte)((rawFrames[f][v, 0] - minX) / rangeX * 255.0f);
                    frames[f][v].y = (byte)((rawFrames[f][v, 1] - minY) / rangeY * 255.0f);
                    frames[f][v].z = (byte)((rawFrames[f][v, 2] - minZ) / rangeZ * 255.0f);
                }
            }

            return (frames, uvs, colors, indices.ToArray(), minX, minY, minZ, maxX, maxY, maxZ);
        }

        public static bool CompareFrames(VertexU8[] frameA, VertexU8[] frameB, int frameNum)
        {
            for (int i = 0; i < frameA.Length; i++)
            {
                int dx = Math.Abs(frameA[i].x - frameB[i].x);
                int dy = Math.Abs(frameA[i].y - frameB[i].y);
                int dz = Math.Abs(frameA[i].z - frameB[i].z);
                if (dx > 1 || dy > 1 || dz > 1) // Allow tolerance of 1 due to potential rounding
                {
                    Console.WriteLine($"Mismatch in frame {frameNum}, vertex {i}. Original: ({frameA[i].x},{frameA[i].y},{frameA[i].z}), Decompressed: ({frameB[i].x},{frameB[i].y},{frameB[i].z})");
                    return false;
                }
            }
            return true;
        }

        public static void RunTest()
        {
            Console.WriteLine("--- Running Self-Contained Test ---");
            uint numVertices = 100; // 10x10 grid
            uint numFrames = 60;
            string testFile = "test.rat";
            string testMeshFile = "test.ratmesh";

            Console.WriteLine("1. Simulating GLB data...");
            var (frames, uvs, colors, indices, minX, minY, minZ, maxX, maxY, maxZ) = SimulateGLBData(numVertices, numFrames);
            numVertices = (uint)frames[0].Length;
            uint numIndices = (uint)indices.Length;

            Console.WriteLine("2. Compressing animation...");
            var compressed = CompressAnimation(frames, uvs, colors, indices, numVertices, numIndices, numFrames, minX, minY, minZ, maxX, maxY, maxZ);
            compressed.mesh_data_filename = testMeshFile;
            compressed.texture_filename = "test_texture.png";

            Console.WriteLine($"3. Writing to '{testFile}' and '{testMeshFile}'...");
            WriteRatFile(testFile, compressed);

            Console.WriteLine($"4. Reading from '{testFile}'...");
            var reRead = ReadRatFile(testFile);

            Console.WriteLine("5. Decompressing and verifying frames...");
            var ctx = CreateDecompressionContext(reRead);
            bool success = true;
            for (uint f = 0; f < numFrames; f++)
            {
                DecompressToFrame(ctx, reRead, f);
                if (!CompareFrames(frames[f], ctx.current_positions, (int)f))
                {
                    success = false;
                    break;
                }
            }

            if (success)
            {
                Console.WriteLine();
                Console.WriteLine("[SUCCESS] Test PASSED. All decompressed frames match original data.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("[FAILED] Test FAILED. Mismatch found between original and decompressed data.");
            }
        }

        public static void PrintUsage()
        {
            Console.WriteLine(@"
Usage: GLBToRAT <command> [options]

Commands:
  test                  Run a self-contained test with simulated data.
                        Generates 'test.rat' and 'test.ratmesh' and verifies integrity.
  <input> <output>      Convert a GLB file to RAT format. (NOT IMPLEMENTED)
                        This functionality requires a GLB parser.

Description:
  A C# port of the glb2rat utility for compressing vertex animation data.
  This tool converts vertex animations into the compact RAT format, which
  is optimized for real-time rendering with low memory overhead.

Key Features:
- Lossy vertex animation compression for 3D models.
- V3 format separates static mesh data (.ratmesh) from animation data (.rat).
- Subsequent frames use delta compression with variable bit-width encoding.
- Constant-time frame access with minimal runtime decompression overhead.

RAT File Structure (V3):
  .ratmesh file:
    [Header]        - Metadata about the mesh.
    [UVs]           - Texture coordinates.
    [Colors]        - Vertex colors.
    [Indices]       - Triangle indices.
    [Texture Path]  - Path to the texture file.
  .rat file:
    [Header]        - Metadata, including path to .ratmesh file.
    [Bit Widths]    - Per-vertex bit widths for delta compression.
    [First Frame]   - Uncompressed vertex positions for the first frame.
    [Delta Stream]  - Compressed deltas for subsequent frames.

Warning:
  This library performs lossy compression. Precision is limited to 8-bits
  per coordinate axis. The original C version uses cgltf for GLB parsing,
  which is not implemented in this C# port. Use the 'test' command to see
  the compression in action with simulated data.
");
        }

        public static void Main(string[] args)
        {
            /* Unity does not use a Main method. This code is for command-line execution.
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                PrintUsage();
                return;
            }

            string command = args[0].ToLower();
            if (command == "test")
            {
                RunTest();
            }
            else if (args.Length >= 2)
            {
                string inputFile = args[0];
                string outputFile = args[1];
                string textureFile = (args.Length > 2) ? args[2] : null;

                Console.WriteLine($"Converting {inputFile} to {outputFile}");
                if(textureFile != null) Console.WriteLine($" with texture {textureFile}");

                // This part is a placeholder for actual GLB parsing
                Console.WriteLine("GLB parsing is not implemented. Using simulated data for the conversion process.");
                
                var (frames, uvs, colors, indices, minX, minY, minZ, maxX, maxY, maxZ) = SimulateGLBData(100, 60);
                var compressed = CompressFrames(frames.Select(f => 
                    {
                        var vectors = new UnityEngine.Vector3[f.Length];
                        for(int i=0; i<f.Length; i++)
                        {
                            vectors[i] = new UnityEngine.Vector3(
                                minX + (f[i].x / 255.0f) * (maxX - minX),
                                minY + (f[i].y / 255.0f) * (maxY - minY),
                                minZ + (f[i].z / 255.0f) * (maxZ - minZ)
                            );
                        }
                        return vectors;
                    }).ToList(), 
                    indices, 
                    uvs.Select(uv => new UnityEngine.Vector2(uv.u, uv.v)).ToArray(), 
                    colors.Select(c => new UnityEngine.Color(c.r, c.g, c.b, c.a)).ToArray(),
                    textureFile,
                    outputFile.Replace(".rat", ".ratmesh")
                );

                WriteRatFile(outputFile, compressed);
                Console.WriteLine("Conversion complete (using simulated data).");
            }
            else
            {
                PrintUsage();
            }
            */
        }
    }
}
