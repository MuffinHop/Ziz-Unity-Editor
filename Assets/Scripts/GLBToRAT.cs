using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

// --- Data Structures ---
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexU8
{
    public byte x, y, z;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexUV
{
    public float u, v;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VertexColor
{
    public float r, g, b, a;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RatHeader
{
    public uint magic;
    public uint num_vertices;
    public uint num_frames;
    public uint num_indices;
    public uint uv_offset;
    public uint color_offset;
    public uint indices_offset;
    public uint bit_widths_offset;
    public uint delta_offset;
    public float min_x, min_y, min_z;
    public float max_x, max_y, max_z;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public byte[] reserved;
}

public class CompressedAnimation
{
    public uint num_vertices;
    public uint num_frames;
    public uint num_indices;
    public VertexUV[] uvs;
    public VertexColor[] colors;
    public ushort[] indices;
    public uint[] delta_stream;
    public VertexU8[] first_frame;
    public float min_x, min_y, min_z;
    public float max_x, max_y, max_z;
    public byte[] bit_widths_x;
    public byte[] bit_widths_y;
    public byte[] bit_widths_z;
}

public class DecompressionContext
{
    public VertexU8[] current_positions;
    public uint current_frame;
}

public class BitstreamWriter
{
    private readonly List<uint> _stream = new List<uint>();
    private uint _currentWord = 0;
    private int _bitsUsed = 0;

    public void Write(uint value, int bits)
    {
        if (bits <= 0 || bits > 32) throw new ArgumentException("Bits must be between 1 and 32.");
        if (bits < 32) value &= (uint)((1L << bits) - 1);

        int bitsRemainingInWord = 32 - _bitsUsed;

        if (bits < bitsRemainingInWord)
        {
            _currentWord |= value << (bitsRemainingInWord - bits);
            _bitsUsed += bits;
        }
        else
        {
            _currentWord |= value >> (bits - bitsRemainingInWord);
            _stream.Add(_currentWord);

            bits -= bitsRemainingInWord;
            _bitsUsed = bits;
            _currentWord = (bits > 0) ? value << (32 - bits) : 0;
        }
    }

    public void Flush()
    {
        if (_bitsUsed > 0)
        {
            _stream.Add(_currentWord);
        }
        _currentWord = 0;
        _bitsUsed = 0;
    }

    public uint[] ToArray() => _stream.ToArray();
}

public class BitstreamReader
{
    private readonly uint[] _stream;
    private uint _currentWord;
    private int _bitsReadFromWord = 0;
    private int _position = 0;

    public BitstreamReader(uint[] stream)
    {
        _stream = stream;
        if (_stream != null && _stream.Length > 0)
        {
            _currentWord = _stream[0];
        }
    }

    public uint Read(int bits)
    {
        if (bits < 0 || bits > 32) throw new ArgumentException("Bits must be between 0 and 32.");
        if (bits == 0) return 0;

        uint result;
        int bitsRemainingInWord = 32 - _bitsReadFromWord;

        if (bits <= bitsRemainingInWord)
        {
            result = (_currentWord >> (bitsRemainingInWord - bits)) & (uint)((1L << bits) - 1);
            _bitsReadFromWord += bits;
        }
        else
        {
            result = (_currentWord & (uint)((1L << bitsRemainingInWord) - 1)) << (bits - bitsRemainingInWord);
            
            _position++;
            if (_position < _stream.Length)
            {
                _currentWord = _stream[_position];
                _bitsReadFromWord = bits - bitsRemainingInWord;
                result |= _currentWord >> (32 - _bitsReadFromWord);
            }
        }
        
        if (_bitsReadFromWord == 32)
        {
            _position++;
            if (_position < _stream.Length)
            {
                _currentWord = _stream[_position];
            }
            _bitsReadFromWord = 0;
        }
        return result;
    }
}

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
            using var stream = File.Open(filename, FileMode.Create);
            WriteRatFile(stream, anim);
        }

        public static void WriteRatFile(Stream stream, CompressedAnimation anim)
        {
            using var writer = new BinaryWriter(stream);

            uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeader));
            uint uvSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexUV));
            uint colorSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexColor));
            uint indicesSize = anim.num_indices * sizeof(ushort);
            uint bitWidthsSize = anim.num_vertices * 3;
            uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));

            var header = new RatHeader
            {
                magic = 0x31544152, // "RAT1"
                num_vertices = anim.num_vertices,
                num_frames = anim.num_frames,
                num_indices = anim.num_indices,
                min_x = anim.min_x, max_x = anim.max_x,
                min_y = anim.min_y, max_y = anim.max_y,
                min_z = anim.min_z, max_z = anim.max_z,
                uv_offset = headerSize,
                color_offset = headerSize + uvSize,
                indices_offset = headerSize + uvSize + colorSize,
                bit_widths_offset = headerSize + uvSize + colorSize + indicesSize,
                delta_offset = headerSize + uvSize + colorSize + indicesSize + bitWidthsSize + firstFrameSize,
                reserved = new byte[4]
            };

            byte[] headerBytes = new byte[headerSize];
            IntPtr ptr = Marshal.AllocHGlobal((int)headerSize);
            Marshal.StructureToPtr(header, ptr, false);
            Marshal.Copy(ptr, headerBytes, 0, (int)headerSize);
            Marshal.FreeHGlobal(ptr);
            writer.Write(headerBytes);

            foreach (var uv in anim.uvs) { writer.Write(uv.u); writer.Write(uv.v); }
            foreach (var color in anim.colors) { writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a); }
            if (anim.indices.Length > 0) foreach (var index in anim.indices) writer.Write(index);
            
            writer.Write(anim.bit_widths_x);
            writer.Write(anim.bit_widths_y);
            writer.Write(anim.bit_widths_z);

            foreach (var v in anim.first_frame) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            if (anim.delta_stream.Length > 0) foreach (var word in anim.delta_stream) writer.Write(word);
        }

        public static CompressedAnimation ReadRatFile(string filename)
        {
            using var stream = File.OpenRead(filename);
            return ReadRatFile(stream);
        }

        public static CompressedAnimation ReadRatFile(Stream stream)
        {
            using var reader = new BinaryReader(stream);

            byte[] headerBytes = reader.ReadBytes(Marshal.SizeOf(typeof(RatHeader)));
            IntPtr ptr = Marshal.AllocHGlobal(headerBytes.Length);
            Marshal.Copy(headerBytes, 0, ptr, headerBytes.Length);
            var header = (RatHeader)Marshal.PtrToStructure(ptr, typeof(RatHeader));
            Marshal.FreeHGlobal(ptr);

            var anim = new CompressedAnimation
            {
                num_vertices = header.num_vertices,
                num_frames = header.num_frames,
                num_indices = header.num_indices,
                min_x = header.min_x, max_x = header.max_x,
                min_y = header.min_y, max_y = header.max_y,
                min_z = header.min_z, max_z = header.max_z
            };

            reader.BaseStream.Seek(header.uv_offset, SeekOrigin.Begin);
            anim.uvs = new VertexUV[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++) anim.uvs[i] = new VertexUV { u = reader.ReadSingle(), v = reader.ReadSingle() };

            reader.BaseStream.Seek(header.color_offset, SeekOrigin.Begin);
            anim.colors = new VertexColor[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++) anim.colors[i] = new VertexColor { r = reader.ReadSingle(), g = reader.ReadSingle(), b = reader.ReadSingle(), a = reader.ReadSingle() };

            reader.BaseStream.Seek(header.indices_offset, SeekOrigin.Begin);
            anim.indices = new ushort[anim.num_indices];
            for (int i = 0; i < anim.num_indices; i++) anim.indices[i] = reader.ReadUInt16();

            reader.BaseStream.Seek(header.bit_widths_offset, SeekOrigin.Begin);
            anim.bit_widths_x = reader.ReadBytes((int)anim.num_vertices);
            anim.bit_widths_y = reader.ReadBytes((int)anim.num_vertices);
            anim.bit_widths_z = reader.ReadBytes((int)anim.num_vertices);

            long firstFrameOffset = header.bit_widths_offset + (anim.num_vertices * 3);
            reader.BaseStream.Seek(firstFrameOffset, SeekOrigin.Begin);
            anim.first_frame = new VertexU8[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++) anim.first_frame[i] = new VertexU8 { x = reader.ReadByte(), y = reader.ReadByte(), z = reader.ReadByte() };

            reader.BaseStream.Seek(header.delta_offset, SeekOrigin.Begin);
            long deltaStreamByteSize = reader.BaseStream.Length - header.delta_offset;
            anim.delta_stream = new uint[deltaStreamByteSize / 4];
            for (int i = 0; i < anim.delta_stream.Length; i++) anim.delta_stream[i] = reader.ReadUInt32();

            return anim;
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

            Console.WriteLine("1. Simulating GLB data...");
            var (frames, uvs, colors, indices, minX, minY, minZ, maxX, maxY, maxZ) = SimulateGLBData(numVertices, numFrames);
            numVertices = (uint)frames[0].Length;
            uint numIndices = (uint)indices.Length;

            Console.WriteLine("2. Compressing animation...");
            var compressed = CompressAnimation(frames, uvs, colors, indices, numVertices, numIndices, numFrames, minX, minY, minZ, maxX, maxY, maxZ);

            Console.WriteLine($"3. Writing to '{testFile}'...");
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
                        Generates 'test.rat' and verifies its integrity.
  <input> <output>      Convert a GLB file to RAT format. (NOT IMPLEMENTED)
                        This functionality requires a GLB parser.

Description:
  A C# port of the glb2rat utility for compressing vertex animation data.
  This tool converts vertex animations into the compact RAT format, which
  is optimized for real-time rendering with low memory overhead.

Key Features:
- Lossy vertex animation compression for 3D models.
- Optimized for real-time rendering and low-memory environments.
- Subsequent frames use delta compression with variable bit-width encoding.
- Constant-time frame access with minimal runtime decompression overhead.

RAT File Structure:
  [Header]        - Metadata about the animation.
  [UVs]           - Texture coordinates.
  [Colors]        - Vertex colors.
  [Indices]       - Triangle indices.
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
            else if (args.Length == 2)
            {
                Console.WriteLine("GLB parsing is not implemented in this C# port.");
                Console.WriteLine("Please use the 'test' command to run a demonstration.");
            }
            else
            {
                PrintUsage();
            }
            */
        }
    }
}
