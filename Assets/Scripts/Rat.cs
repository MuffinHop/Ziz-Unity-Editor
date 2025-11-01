using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Rat
{
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
        public uint magic;              // "RAT3" = 0x33544152
        public uint num_vertices;
        public uint num_frames;
        public uint num_indices;
        public uint delta_offset;
        public uint bit_widths_offset; // Offset to bit widths array
        public uint mesh_data_filename_offset; // Offset to mesh data filename
        public uint mesh_data_filename_length; // Length of mesh data filename
        public float min_x, min_y, min_z;
        public float max_x, max_y, max_z;
        public byte is_first_frame_raw; // 0 = false, 1 = true
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] reserved; // Was 8, now 3 to make space for the new offset
        public uint raw_first_frame_offset; // Offset to raw first frame data
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RatMeshHeader
    {
        public uint magic;              // "RATM" = 0x4D544152
        public uint num_vertices;
        public uint num_indices;
        public uint uv_offset;
        public uint color_offset;
        public uint indices_offset;
        public uint texture_filename_offset;
        public uint texture_filename_length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] reserved;
    }

    public class RatMeshData
    {
        public VertexUV[] uvs;
        public VertexColor[] colors;
        public ushort[] indices;
        public string texture_filename = "";
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
        public UnityEngine.Vector3[] first_frame_raw; // For uncompressed first frame
        public bool isFirstFrameRaw = false;
        public float min_x, min_y, min_z;
        public float max_x, max_y, max_z;
        public byte[] bit_widths_x;
        public byte[] bit_widths_y;
        public byte[] bit_widths_z;
        public string texture_filename = ""; // V2: Texture filename for this animation
        public string mesh_data_filename = ""; // V3: Filename for the .ratmesh file
    }

    public class DecompressionContext
    {
        public VertexU8[] current_positions;
        public uint current_frame;
    }

    // --- Bitstream Handling ---
    internal class BitstreamReader
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

    // --- Bitstream Handling ---
    internal class BitstreamWriter
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

    // --- Core Logic ---
    public static class Core
    {
        private static int SignExtend(uint value, int bits)
        {
            if (bits == 0) return 0;
            uint signBit = 1U << (bits - 1);
            return (value & signBit) != 0 ? (int)(value | (~0U << bits)) : (int)value;
        }

        public static CompressedAnimation ReadRatFile(string filepath)
        {
            using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                return ReadRatFile(stream, filepath);
            }
        }

        public static CompressedAnimation ReadRatFile(Stream stream, string filepath = null)
        {
            return ReadRatFileV3(stream, filepath);
        }

        public static RatMeshData ReadRatMeshFile(string filepath)
        {
            using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                return ReadRatMeshFile(stream);
            }
        }

        public static RatMeshData ReadRatMeshFile(Stream stream)
        {
            using (var reader = new BinaryReader(stream))
            {
                byte[] headerBytes = reader.ReadBytes(Marshal.SizeOf(typeof(RatMeshHeader)));
                IntPtr ptr = Marshal.AllocHGlobal(headerBytes.Length);
                Marshal.Copy(headerBytes, 0, ptr, headerBytes.Length);
                var header = (RatMeshHeader)Marshal.PtrToStructure(ptr, typeof(RatMeshHeader));
                Marshal.FreeHGlobal(ptr);

                if (header.magic != 0x4D544152) // "RATM"
                    throw new Exception("Invalid RATM file format.");

                var meshData = new RatMeshData
                {
                    uvs = new VertexUV[header.num_vertices],
                    colors = new VertexColor[header.num_vertices],
                    indices = new ushort[header.num_indices]
                };

                reader.BaseStream.Seek(header.uv_offset, SeekOrigin.Begin);
                for (int i = 0; i < header.num_vertices; i++)
                {
                    meshData.uvs[i].u = reader.ReadSingle();
                    meshData.uvs[i].v = reader.ReadSingle();
                }

                reader.BaseStream.Seek(header.color_offset, SeekOrigin.Begin);
                for (int i = 0; i < header.num_vertices; i++)
                {
                    meshData.colors[i].r = reader.ReadSingle();
                    meshData.colors[i].g = reader.ReadSingle();
                    meshData.colors[i].b = reader.ReadSingle();
                    meshData.colors[i].a = reader.ReadSingle();
                }

                reader.BaseStream.Seek(header.indices_offset, SeekOrigin.Begin);
                for (int i = 0; i < header.num_indices; i++)
                {
                    meshData.indices[i] = reader.ReadUInt16();
                }

                if (header.texture_filename_length > 0)
                {
                    reader.BaseStream.Seek(header.texture_filename_offset, SeekOrigin.Begin);
                    byte[] textureBytes = reader.ReadBytes((int)header.texture_filename_length);
                    meshData.texture_filename = System.Text.Encoding.UTF8.GetString(textureBytes);
                }

                return meshData;
            }
        }

        public static CompressedAnimation ReadRatFileV3(Stream stream, string filepath)
        {
            using (var reader = new BinaryReader(stream))
            {
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
                    min_z = header.min_z, max_z = header.max_z,
                    first_frame = new VertexU8[header.num_vertices],
                    bit_widths_x = new byte[header.num_vertices],
                    bit_widths_y = new byte[header.num_vertices],
                    bit_widths_z = new byte[header.num_vertices],
                    isFirstFrameRaw = header.is_first_frame_raw == 1
                };

                if (header.mesh_data_filename_length > 0)
                {
                    reader.BaseStream.Seek(header.mesh_data_filename_offset, SeekOrigin.Begin);
                    byte[] filenameBytes = reader.ReadBytes((int)header.mesh_data_filename_length);
                    anim.mesh_data_filename = System.Text.Encoding.UTF8.GetString(filenameBytes);

                    string meshPath = Path.Combine(Path.GetDirectoryName(filepath), anim.mesh_data_filename);
                    if (File.Exists(meshPath))
                    {
                        var meshData = ReadRatMeshFile(meshPath);
                        anim.uvs = meshData.uvs;
                        anim.colors = meshData.colors;
                        anim.indices = meshData.indices;
                        anim.texture_filename = meshData.texture_filename;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Could not find .ratmesh file: {meshPath}");
                    }
                }

                reader.BaseStream.Seek(header.bit_widths_offset, SeekOrigin.Begin);
                reader.Read(anim.bit_widths_x, 0, anim.bit_widths_x.Length);
                reader.Read(anim.bit_widths_y, 0, anim.bit_widths_y.Length);
                reader.Read(anim.bit_widths_z, 0, anim.bit_widths_z.Length);

                long firstFrameOffset = header.bit_widths_offset + (header.num_vertices * 3);
                reader.BaseStream.Seek(firstFrameOffset, SeekOrigin.Begin);
                for (int i = 0; i < header.num_vertices; i++)
                {
                    anim.first_frame[i].x = reader.ReadByte();
                    anim.first_frame[i].y = reader.ReadByte();
                    anim.first_frame[i].z = reader.ReadByte();
                }

                if (anim.isFirstFrameRaw && header.raw_first_frame_offset > 0)
                {
                    anim.first_frame_raw = new UnityEngine.Vector3[header.num_vertices];
                    reader.BaseStream.Seek(header.raw_first_frame_offset, SeekOrigin.Begin);
                    for (int i = 0; i < header.num_vertices; i++)
                    {
                        anim.first_frame_raw[i].x = reader.ReadSingle();
                        anim.first_frame_raw[i].y = reader.ReadSingle();
                        anim.first_frame_raw[i].z = reader.ReadSingle();
                    }
                }

                reader.BaseStream.Seek(header.delta_offset, SeekOrigin.Begin);
                int deltaWords = (int)((stream.Length - header.delta_offset) / 4);
                anim.delta_stream = new uint[deltaWords];
                for (int i = 0; i < deltaWords; i++)
                {
                    anim.delta_stream[i] = reader.ReadUInt32();
                }

                return anim;
            }
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
    }

    public static class Tool
    {
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

        public static void WriteRatFile(Stream stream, CompressedAnimation anim, string meshDataFilename = null)
        {
            if (string.IsNullOrEmpty(meshDataFilename) && !string.IsNullOrEmpty(anim.mesh_data_filename))
            {
                meshDataFilename = anim.mesh_data_filename;
            }
            WriteRatFileV3(stream, anim, meshDataFilename);
        }
        
        public static void WriteRatMeshFile(Stream stream, CompressedAnimation anim)
        {
            using (var writer = new BinaryWriter(stream))
            {
                uint headerSize = (uint)Marshal.SizeOf(typeof(RatMeshHeader));
                uint uvSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexUV));
                uint colorSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexColor));
                uint indicesSize = anim.num_indices * sizeof(ushort);
                byte[] textureFilenameBytes = System.Text.Encoding.UTF8.GetBytes(anim.texture_filename ?? "");

                var header = new RatMeshHeader
                {
                    magic = 0x4D544152, // "RATM"
                    num_vertices = anim.num_vertices,
                    num_indices = anim.num_indices,
                    uv_offset = headerSize,
                    color_offset = headerSize + uvSize,
                    indices_offset = headerSize + uvSize + colorSize,
                    texture_filename_offset = headerSize + uvSize + colorSize + indicesSize,
                    texture_filename_length = (uint)textureFilenameBytes.Length,
                    reserved = new byte[16]
                };

                byte[] headerBytes = new byte[headerSize];
                IntPtr ptr = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(header, ptr, false);
                Marshal.Copy(ptr, headerBytes, 0, (int)headerSize);
                Marshal.FreeHGlobal(ptr);
                writer.Write(headerBytes);

                foreach (var uv in anim.uvs) { writer.Write(uv.u); writer.Write(uv.v); }
                foreach (var color in anim.colors) { writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a); }
                foreach (var index in anim.indices) { writer.Write(index); }
                if (textureFilenameBytes.Length > 0) writer.Write(textureFilenameBytes);
            }
        }

        public static void WriteRatFileV3(Stream stream, CompressedAnimation anim, string meshDataFilename)
        {
            using (var writer = new BinaryWriter(stream))
            {
                uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeader));
                uint bitWidthsSize = anim.num_vertices * 3;
                uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));
                byte[] meshDataFilenameBytes = System.Text.Encoding.UTF8.GetBytes(meshDataFilename);
                uint rawFirstFrameSize = anim.isFirstFrameRaw ? anim.num_vertices * (uint)Marshal.SizeOf(typeof(UnityEngine.Vector3)) : 0;

                var header = new RatHeader
                {
                    magic = 0x33544152, // "RAT3"
                    num_vertices = anim.num_vertices,
                    num_frames = anim.num_frames,
                    num_indices = anim.num_indices,
                    min_x = anim.min_x, max_x = anim.max_x,
                    min_y = anim.min_y, max_y = anim.max_y,
                    min_z = anim.min_z, max_z = anim.max_z,
                    bit_widths_offset = headerSize,
                    mesh_data_filename_offset = headerSize + bitWidthsSize + firstFrameSize,
                    mesh_data_filename_length = (uint)meshDataFilenameBytes.Length,
                    raw_first_frame_offset = anim.isFirstFrameRaw ? headerSize + bitWidthsSize + firstFrameSize + (uint)meshDataFilenameBytes.Length : 0,
                    delta_offset = headerSize + bitWidthsSize + firstFrameSize + (uint)meshDataFilenameBytes.Length + rawFirstFrameSize,
                    is_first_frame_raw = (byte)(anim.isFirstFrameRaw ? 1 : 0),
                    reserved = new byte[3]
                };

                byte[] headerBytes = new byte[headerSize];
                IntPtr ptr = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(header, ptr, false);
                Marshal.Copy(ptr, headerBytes, 0, (int)headerSize);
                Marshal.FreeHGlobal(ptr);
                writer.Write(headerBytes);

                writer.Write(anim.bit_widths_x);
                writer.Write(anim.bit_widths_y);
                writer.Write(anim.bit_widths_z);

                foreach (var v in anim.first_frame) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                
                writer.Write(meshDataFilenameBytes);

                if (anim.isFirstFrameRaw)
                {
                    foreach (var v in anim.first_frame_raw) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                }

                foreach (var word in anim.delta_stream) { writer.Write(word); }
            }
        }

        /// <summary>
        /// Writes RAT files with automatic size-based splitting at 64KB boundaries.
        /// Throws an exception if the first frame data exceeds 64KB.
        /// </summary>
        /// <param name="baseFilename">Base filename without extension</param>
        /// <param name="anim">Compressed animation data to split and save</param>
        /// <param name="maxFileSizeKB">Maximum file size in KB before splitting (default: 64KB)</param>
        /// <returns>List of created filenames</returns>
        public static List<string> WriteRatFileWithSizeSplitting(string baseFilename, CompressedAnimation anim, int maxFileSizeKB = 64)
        {
            const int KB = 1024;
            int maxFileSize = maxFileSizeKB * KB;
            var createdFiles = new List<string>();

            // V3: Write the separate .ratmesh file first, it is not split.
            string meshDataFilename = $"{baseFilename}.ratmesh";
            if (!string.IsNullOrEmpty(anim.mesh_data_filename))
            {
                using (var meshStream = new FileStream(meshDataFilename, FileMode.Create))
                {
                    WriteRatMeshFile(meshStream, anim);
                }
                createdFiles.Add(meshDataFilename);
                UnityEngine.Debug.Log($"Created RAT mesh file: {meshDataFilename}");
            }

            // Calculate static data sizes for the .rat file (V3 format)
            uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeader));
            uint bitWidthsSize = anim.num_vertices * 3;
            uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));
            byte[] meshDataFilenameBytes = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(meshDataFilename));
            uint rawFirstFrameSize = anim.isFirstFrameRaw ? anim.num_vertices * (uint)Marshal.SizeOf(typeof(UnityEngine.Vector3)) : 0;
            
            // Calculate static overhead (everything except delta stream)
            uint staticOverheadSize = headerSize + bitWidthsSize + firstFrameSize + (uint)meshDataFilenameBytes.Length + rawFirstFrameSize;
            
            // Check if the static data alone exceeds the size limit
            if (staticOverheadSize > maxFileSize)
            {
                throw new System.InvalidOperationException(
                    $"ERROR: Static RAT data ({staticOverheadSize} bytes) exceeds maximum file size ({maxFileSize} bytes)!\n" +
                    $"Breakdown: Header({headerSize}) + BitWidths({bitWidthsSize}) + FirstFrame({firstFrameSize}) + Filename({meshDataFilenameBytes.Length}) + RawFirstFrame({rawFirstFrameSize})\n" +
                    $"Consider reducing mesh complexity or increasing maxFileSizeKB parameter.");
            }
            
            // If single frame or no delta data, create a single .rat file
            if (anim.num_frames <= 1 || anim.delta_stream == null || anim.delta_stream.Length == 0)
            {
                string filename = $"{baseFilename}.rat";
                using (var stream = new FileStream(filename, FileMode.Create))
                {
                    WriteRatFile(stream, anim, Path.GetFileName(meshDataFilename));
                }
                createdFiles.Add(filename);
                UnityEngine.Debug.Log($"Created single RAT file: {filename} ({new FileInfo(filename).Length} bytes)");
                return createdFiles;
            }
            
            // Calculate available space for delta data per chunk
            uint availableSpaceForDeltas = (uint)(maxFileSize - staticOverheadSize);
            uint deltaStreamWordSize = sizeof(uint);
            
            // Calculate how to split the delta stream
            uint totalDeltaWords = (uint)anim.delta_stream.Length;
            uint maxDeltaWordsPerChunk = availableSpaceForDeltas / deltaStreamWordSize;
            
            if (maxDeltaWordsPerChunk == 0)
            {
                throw new System.InvalidOperationException(
                    $"ERROR: Cannot fit any delta data within {maxFileSizeKB}KB limit! Static overhead is {staticOverheadSize} bytes, " +
                    $"leaving only {availableSpaceForDeltas} bytes for deltas, but need at least {deltaStreamWordSize} bytes per delta word.");
            }
            
            int numberOfChunks = UnityEngine.Mathf.CeilToInt((float)totalDeltaWords / maxDeltaWordsPerChunk);
            
            UnityEngine.Debug.Log($"Splitting RAT animation into {numberOfChunks} chunks (max {maxFileSizeKB}KB each):");
            
            for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
            {
                uint startDeltaWord = (uint)(chunkIndex * maxDeltaWordsPerChunk);
                uint endDeltaWord = System.Math.Min(startDeltaWord + maxDeltaWordsPerChunk, totalDeltaWords);
                uint chunkDeltaWords = endDeltaWord - startDeltaWord;
                
                var chunkAnim = new CompressedAnimation
                {
                    num_vertices = anim.num_vertices,
                    num_indices = anim.num_indices,
                    num_frames = anim.num_frames, // This should reflect the frames in the chunk, but the header needs total frames.
                    min_x = anim.min_x, max_x = anim.max_x,
                    min_y = anim.min_y, max_y = anim.max_y,
                    min_z = anim.min_z, max_z = anim.max_z,
                    first_frame = anim.first_frame,
                    first_frame_raw = anim.first_frame_raw,
                    isFirstFrameRaw = anim.isFirstFrameRaw,
                    bit_widths_x = anim.bit_widths_x,
                    bit_widths_y = anim.bit_widths_y,
                    bit_widths_z = anim.bit_widths_z,
                    mesh_data_filename = Path.GetFileName(meshDataFilename)
                };
                
                if (chunkDeltaWords > 0)
                {
                    chunkAnim.delta_stream = new uint[chunkDeltaWords];
                    System.Array.Copy(anim.delta_stream, startDeltaWord, chunkAnim.delta_stream, 0, chunkDeltaWords);
                }
                else
                {
                    chunkAnim.delta_stream = new uint[0];
                }
                
                string filename = (numberOfChunks == 1)
                    ? $"{baseFilename}.rat"
                    : $"{baseFilename}_part{chunkIndex + 1:D2}of{numberOfChunks:D2}.rat";
                
                using (var stream = new FileStream(filename, FileMode.Create))
                {
                    WriteRatFileV3(stream, chunkAnim, chunkAnim.mesh_data_filename);
                }
                
                createdFiles.Add(filename);
                UnityEngine.Debug.Log($"  Chunk {chunkIndex + 1}: {filename} ({new FileInfo(filename).Length} bytes)");
            }
            
            return createdFiles;
        }
        
        /// <summary>
        /// Validates that a mesh is compatible with the RAT format constraints.
        /// </summary>
        /// <param name="sourceMesh">The mesh to validate</param>
        /// <returns>True if valid, false if incompatible</returns>
        private static bool ValidateMeshForRAT(UnityEngine.Mesh sourceMesh)
        {
            if (sourceMesh == null)
            {
                UnityEngine.Debug.LogError("Source mesh is null.");
                return false;
            }

            uint numVertices = (uint)sourceMesh.vertexCount;
            
            // Validate mesh compatibility with RAT format (ushort index limitation)
            if (numVertices > 65535)
            {
                UnityEngine.Debug.LogError($"Mesh has {numVertices} vertices, but RAT format only supports up to 65,535 vertices due to ushort index limitation. Consider using a mesh with fewer vertices or implement uint32 indices.");
                return false;
            }

            // Validate triangle indices
            var sourceIndices = sourceMesh.triangles;
            if (sourceIndices == null)
            {
                UnityEngine.Debug.LogError("Mesh triangles array is null. Invalid mesh data.");
                return false;
            }
            
            // Debug: Log detailed mesh information
            UnityEngine.Debug.Log($"RAT Validation: Mesh analysis - Vertices: {numVertices}, Triangle indices array length: {sourceIndices.Length}, Expected triangles: {sourceIndices.Length / 3}");
            
            if (sourceIndices.Length % 3 != 0)
            {
                UnityEngine.Debug.LogError($"Triangle indices count ({sourceIndices.Length}) is not divisible by 3. Each triangle requires exactly 3 indices. " +
                                          $"This means the mesh has malformed triangle data. Triangle count should be {sourceIndices.Length / 3} but the remainder is {sourceIndices.Length % 3}.");
                return false;
            }
            for (int i = 0; i < sourceIndices.Length; i++)
            {
                if (sourceIndices[i] > 65535)
                {
                    UnityEngine.Debug.LogError($"Triangle index {sourceIndices[i]} at position {i} exceeds ushort limit (65535). This mesh is incompatible with the current RAT format.");
                    return false;
                }
                if (sourceIndices[i] < 0)
                {
                    UnityEngine.Debug.LogError($"Triangle index {sourceIndices[i]} at position {i} is negative. Invalid mesh data.");
                    return false;
                }
                if (sourceIndices[i] >= numVertices)
                {
                    UnityEngine.Debug.LogError($"Triangle index {sourceIndices[i]} at position {i} references vertex {sourceIndices[i]}, but mesh only has {numVertices} vertices (valid range: 0-{numVertices - 1}).");
                    return false;
                }
            }
            
            return true;
        }


        
        /// <summary>
        /// Compress animation data from frames with static UV and color data.
        /// </summary>
        /// <param name="rawFrames">Vertex position data per frame</param>
        /// <param name="sourceMesh">Source mesh for fallback UV/color data and indices</param>
        /// <param name="staticUVs">Static UV data (null for source mesh UVs)</param>
        /// <param name="staticColors">Static color data (null for source mesh colors)</param>
        /// <returns>Compressed animation data</returns>
        public static CompressedAnimation CompressFromFrames(
            List<UnityEngine.Vector3[]> rawFrames,
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] staticUVs,
            UnityEngine.Color[] staticColors,
            bool preserveFirstFrame = false)
        {
            if (rawFrames == null || rawFrames.Count == 0) return null;
            if (!ValidateMeshForRAT(sourceMesh)) return null;

            uint numVertices = (uint)sourceMesh.vertexCount;
            uint numFrames = (uint)rawFrames.Count;
            uint numIndices = (uint)sourceMesh.triangles.Length;

            // Get triangle indices for use throughout the method
            var sourceIndices = sourceMesh.triangles;

            // 1. Find animation bounds - Manual calculation to avoid Unity Bounds quirks
            UnityEngine.Vector3 minBounds = rawFrames[0][0];
            UnityEngine.Vector3 maxBounds = rawFrames[0][0];
            
            foreach (var frame in rawFrames)
            {
                foreach (var v in frame)
                {
                    if (v.x < minBounds.x) minBounds.x = v.x;
                    if (v.y < minBounds.y) minBounds.y = v.y;
                    if (v.z < minBounds.z) minBounds.z = v.z;
                    if (v.x > maxBounds.x) maxBounds.x = v.x;
                    if (v.y > maxBounds.y) maxBounds.y = v.y;
                    if (v.z > maxBounds.z) maxBounds.z = v.z;
                }
            }
            
            // Debug: Log the calculated bounds
            UnityEngine.Debug.Log($"Compression bounds: Min({minBounds.x:F3}, {minBounds.y:F3}, {minBounds.z:F3}) Max({maxBounds.x:F3}, {maxBounds.y:F3}, {maxBounds.z:F3})");

            // 2. Quantize frames to 8-bit
            var quantizedFrames = new VertexU8[numFrames][];
            var range = maxBounds - minBounds;
            if (range.x == 0) range.x = 1;
            if (range.y == 0) range.y = 1;
            if (range.z == 0) range.z = 1;

            for (int f = 0; f < numFrames; f++)
            {
                quantizedFrames[f] = new VertexU8[numVertices];
                for (int v = 0; v < numVertices; v++)
                {
                    quantizedFrames[f][v].x = (byte)UnityEngine.Mathf.RoundToInt(255 * ((rawFrames[f][v].x - minBounds.x) / range.x));
                    quantizedFrames[f][v].y = (byte)UnityEngine.Mathf.RoundToInt(255 * ((rawFrames[f][v].y - minBounds.y) / range.y));
                    quantizedFrames[f][v].z = (byte)UnityEngine.Mathf.RoundToInt(255 * ((rawFrames[f][v].z - minBounds.z) / range.z));
                }
            }

            // 3. Handle UVs, Colors, and Indices from source mesh
            var uvs = new VertexUV[numVertices];
            var colors = new VertexColor[numVertices];
            var indices = new ushort[numIndices];
            
            var sourceUVs = staticUVs ?? sourceMesh.uv;
            var sourceColors = staticColors ?? sourceMesh.colors;

            for (int i = 0; i < numVertices; i++) 
            {
                if(sourceUVs.Length > i) 
                {
                    uvs[i] = new VertexUV { 
                        u = sourceUVs[i].x,
                        v = sourceUVs[i].y
                    };
                }
                
                if(sourceColors.Length > i) 
                {
                    colors[i] = new VertexColor { 
                        r = sourceColors[i].r,
                        g = sourceColors[i].g,
                        b = sourceColors[i].b,
                        a = sourceColors[i].a
                    };
                }
                else
                {
                    colors[i] = new VertexColor { r = 1, g = 1, b = 1, a = 1 };
                }
            }
            for (int i = 0; i < numIndices; i++) indices[i] = (ushort)sourceIndices[i];

            var anim = new CompressedAnimation
            {
                num_vertices = numVertices,
                num_indices = numIndices,
                num_frames = numFrames,
                min_x = minBounds.x, min_y = minBounds.y, min_z = minBounds.z,
                max_x = maxBounds.x, max_y = maxBounds.y, max_z = maxBounds.z,
                first_frame = new VertexU8[numVertices],
                uvs = uvs,
                colors = colors,
                indices = indices,
                bit_widths_x = new byte[numVertices],
                bit_widths_y = new byte[numVertices],
                bit_widths_z = new byte[numVertices],
                isFirstFrameRaw = preserveFirstFrame
            };

            Array.Copy(quantizedFrames[0], anim.first_frame, numVertices);

            if (preserveFirstFrame)
            {
                anim.first_frame_raw = new UnityEngine.Vector3[numVertices];
                Array.Copy(rawFrames[0], anim.first_frame_raw, numVertices);
            }

            if (numFrames > 1)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    int maxDx = 0, maxDy = 0, maxDz = 0;
                    for (int f = 1; f < numFrames; f++)
                    {
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        if (Math.Abs(dx) > maxDx) maxDx = Math.Abs(dx);
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        if (Math.Abs(dy) > maxDy) maxDy = Math.Abs(dy);
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;
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
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        writer.Write((uint)dx, anim.bit_widths_x[v]);
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        writer.Write((uint)dy, anim.bit_widths_y[v]);
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;
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
            
            UnityEngine.Debug.Log($"Final stored bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            
            return anim;
        }

        /// <summary>
        /// Compress animation data from frames with static UV and color data.
        /// </summary>
        /// <param name="rawFrames">Vertex position data per frame</param>
        /// <param name="sourceMesh">Source mesh for fallback UV/color data and indices</param>
        /// <param name="staticUVs">Static UV data (null for source mesh UVs)</param>
        /// <param name="staticColors">Static color data (null for source mesh colors)</param>
        /// <param name="preserveFirstFrame">Whether to preserve the first frame as raw data</param>
        /// <param name="maxBitsX">Maximum bits for X delta encoding</param>
        /// <param name="maxBitsY">Maximum bits for Y delta encoding</param>
        /// <param name="maxBitsZ">Maximum bits for Z delta encoding</param>
        /// <returns>Compressed animation data</returns>
        public static CompressedAnimation CompressFromFrames(
            List<UnityEngine.Vector3[]> rawFrames,
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] staticUVs,
            UnityEngine.Color[] staticColors,
            bool preserveFirstFrame,
            int maxBitsX,
            int maxBitsY,
            int maxBitsZ)
        {
            if (rawFrames == null || rawFrames.Count == 0) return null;
            if (!ValidateMeshForRAT(sourceMesh)) return null;

            uint numVertices = (uint)sourceMesh.vertexCount;
            uint numFrames = (uint)rawFrames.Count;
            uint numIndices = (uint)sourceMesh.triangles.Length;

            var sourceIndices = sourceMesh.triangles;

            // 1. Find animation bounds - Manual calculation to avoid Unity Bounds quirks
            UnityEngine.Vector3 minBounds = rawFrames[0][0];
            UnityEngine.Vector3 maxBounds = rawFrames[0][0];
            
            foreach (var frame in rawFrames)
            {
                foreach (var v in frame)
                {
                    if (v.x < minBounds.x) minBounds.x = v.x;
                    if (v.y < minBounds.y) minBounds.y = v.y;
                    if (v.z < minBounds.z) minBounds.z = v.z;
                    if (v.x > maxBounds.x) maxBounds.x = v.x;
                    if (v.y > maxBounds.y) maxBounds.y = v.y;
                    if (v.z > maxBounds.z) maxBounds.z = v.z;
                }
            }
            
            // Debug: Log the calculated bounds
            UnityEngine.Debug.Log($"Compression bounds: Min({minBounds.x:F3}, {minBounds.y:F3}, {minBounds.z:F3}) Max({maxBounds.x:F3}, {maxBounds.y:F3}, {maxBounds.z:F3})");

            // 2. Quantize frames to 8-bit
            var quantizedFrames = new VertexU8[numFrames][];
            var range = maxBounds - minBounds;
            if (range.x == 0) range.x = 1;
            if (range.y == 0) range.y = 1;
            if (range.z == 0) range.z = 1;

            for (int f = 0; f < numFrames; f++)
            {
                quantizedFrames[f] = new VertexU8[numVertices];
                for (int v = 0; v < numVertices; v++)
                {
                    quantizedFrames[f][v].x = (byte)UnityEngine.Mathf.RoundToInt(255 * ((rawFrames[f][v].x - minBounds.x) / range.x));
                    quantizedFrames[f][v].y = (byte)UnityEngine.Mathf.RoundToInt(255 * ((rawFrames[f][v].y - minBounds.y) / range.y));
                    quantizedFrames[f][v].z = (byte)UnityEngine.Mathf.RoundToInt(255 * ((rawFrames[f][v].z - minBounds.z) / range.z));
                }
            }

            // 3. Handle UVs, Colors, and Indices from source mesh
            var uvs = new VertexUV[numVertices];
            var colors = new VertexColor[numVertices];
            var indices = new ushort[numIndices];
            
            var sourceUVs = staticUVs ?? sourceMesh.uv;
            var sourceColors = staticColors ?? sourceMesh.colors;

            for (int i = 0; i < numVertices; i++) 
            {
                if(sourceUVs.Length > i) 
                {
                    uvs[i] = new VertexUV { 
                        u = sourceUVs[i].x,
                        v = sourceUVs[i].y
                    };
                }
                
                if(sourceColors.Length > i) 
                {
                    colors[i] = new VertexColor { 
                        r = sourceColors[i].r,
                        g = sourceColors[i].g,
                        b = sourceColors[i].b,
                        a = sourceColors[i].a
                    };
                }
                else
                {
                    colors[i] = new VertexColor { r = 1, g = 1, b = 1, a = 1 };
                }
            }
            for (int i = 0; i < numIndices; i++) indices[i] = (ushort)sourceIndices[i];

            var anim = new CompressedAnimation
            {
                num_vertices = numVertices,
                num_indices = numIndices,
                num_frames = numFrames,
                min_x = minBounds.x, min_y = minBounds.y, min_z = minBounds.z,
                max_x = maxBounds.x, max_y = maxBounds.y, max_z = maxBounds.z,
                first_frame = new VertexU8[numVertices],
                uvs = uvs,
                colors = colors,
                indices = indices,
                bit_widths_x = new byte[numVertices],
                bit_widths_y = new byte[numVertices],
                bit_widths_z = new byte[numVertices],
                isFirstFrameRaw = preserveFirstFrame
            };

            Array.Copy(quantizedFrames[0], anim.first_frame, numVertices);

            if (preserveFirstFrame)
            {
                anim.first_frame_raw = new UnityEngine.Vector3[numVertices];
                Array.Copy(rawFrames[0], anim.first_frame_raw, numVertices);
            }

            if (numFrames > 1)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    int maxDx = 0, maxDy = 0, maxDz = 0;
                    for (int f = 1; f < numFrames; f++)
                    {
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        if (Math.Abs(dx) > maxDx) maxDx = Math.Abs(dx);
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        if (Math.Abs(dy) > maxDy) maxDy = Math.Abs(dy);
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;
                        if (Math.Abs(dz) > maxDz) maxDz = Math.Abs(dz);
                    }
                    anim.bit_widths_x[v] = (byte)Math.Min(BitsForDelta(maxDx), maxBitsX);
                    anim.bit_widths_y[v] = (byte)Math.Min(BitsForDelta(maxDy), maxBitsY);
                    anim.bit_widths_z[v] = (byte)Math.Min(BitsForDelta(maxDz), maxBitsZ);
                }

                var writer = new BitstreamWriter();
                for (uint f = 1; f < numFrames; f++)
                {
                    for (uint v = 0; v < numVertices; v++)
                    {
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        writer.Write((uint)dx, anim.bit_widths_x[v]);
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        writer.Write((uint)dy, anim.bit_widths_y[v]);
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;
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
            
            UnityEngine.Debug.Log($"Final stored bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            
            return anim;
        }
    }
}
