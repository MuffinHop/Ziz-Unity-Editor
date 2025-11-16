using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq; 

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

        /// <summary>
        /// Reads a RAT3 format file from disk. Only RAT3 format is supported.
        /// </summary>
        public static CompressedAnimation ReadRatFile(string filepath)
        {
            using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                return ReadRatFile(stream, filepath);
            }
        }

        /// <summary>
        /// Reads a RAT3 format file from a stream. Only RAT3 format is supported.
        /// </summary>
        public static CompressedAnimation ReadRatFile(Stream stream, string filepath = null)
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

                // Note: mesh_data_filename support removed - mesh data now handled by .act files

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

        /// <summary>
        /// Writes a RAT3 format file to a stream. Only RAT3 format is supported.
        /// Mesh data (UVs, colors, indices) is embedded in .act files, not in .ratmodel files.
        /// </summary>
        public static void WriteRatFile(Stream stream, CompressedAnimation anim, string meshDataFilename = null)
        {
            if (string.IsNullOrEmpty(meshDataFilename) && !string.IsNullOrEmpty(anim.mesh_data_filename))
            {
                meshDataFilename = anim.mesh_data_filename;
            }
            WriteRatFileV3(stream, anim, meshDataFilename);
        }
        
        /// <summary>
        /// Internal method to write RAT3 format. Use WriteRatFile() instead.
        /// </summary>
        private static void WriteRatFileV3(Stream stream, CompressedAnimation anim, string meshDataFilename)
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

            // NOTE: Mesh data (UVs, colors, indices) is now embedded in .act files (version 5)
            // We no longer create separate .ratmesh files
            // The .rat file still references the mesh data filename for backward compatibility with C engine

            // Calculate static data sizes for the .rat file (V3 format)
            uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeader));
            uint bitWidthsSize = anim.num_vertices * 3;
            uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));
            string meshDataFilename = !string.IsNullOrEmpty(anim.mesh_data_filename) ? anim.mesh_data_filename : $"{baseFilename}.ratmesh";
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
        /// 
        /// IMPORTANT: This method expects vertex frames that already have transforms applied!
        /// The bounding box is calculated from the INPUT frames and stored in the RAT header.
        /// During playback, vertices are quantized to 8-bit (0-255) and scaled into this bounding box.
        /// 
        /// Pipeline:
        /// 1. Input: Vertex frames (already transformed via matrix multiplication in ExportAnimation)
        /// 2. Calculate bounds from all frames
        /// 3. Quantize vertices to 8-bit using bounds: quantized = (vertex - min) / (max - min) * 255
        /// 4. Calculate per-vertex delta encoding bit widths
        /// 5. Store in RAT: bounds, bit widths, first frame, delta stream
        /// 6. On playback: dequantize using bounds: vertex = min + (quantized / 255) * (max - min)
        /// </summary>
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

            var sourceIndices = sourceMesh.triangles;

            // 1. Calculate animation bounds from ALL frames (including transforms)
            // These bounds will be stored in the RAT header and used for dequantization
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
            
            UnityEngine.Debug.Log($"Compression bounds (from transformed frames): Min({minBounds.x:F3}, {minBounds.y:F3}, {minBounds.z:F3}) Max({maxBounds.x:F3}, {maxBounds.y:F3}, {maxBounds.z:F3})");

            // 2. Quantize ALL frames to 8-bit using the calculated bounds
            // This maps the bounding box to 0-255 for each axis independently
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
                    // Map vertex from world space into 0-255 quantized space using bounds
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
            
            UnityEngine.Debug.Log($"Final RAT bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            UnityEngine.Debug.Log($"Quantization range: X({range.x:F3}) Y({range.y:F3}) Z({range.z:F3})");
            
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

        /// <summary>
        /// Convenience method for GLBToRAT-style frame compression.
        /// Converts Vector3 frames directly to compressed animation.
        /// </summary>
        public static CompressedAnimation CompressFramesFromVectors(
            List<UnityEngine.Vector3[]> allFramesVertices,
            ushort[] allIndices,
            UnityEngine.Vector2[] allUVs,
            UnityEngine.Color[] allColors,
            string textureFilename = null,
            string meshDataFilename = null)
        {
            if (allFramesVertices == null || allFramesVertices.Count == 0)
                throw new ArgumentException("No vertex data provided.");

            // Convert to mesh-based compression
            var dummyMesh = new UnityEngine.Mesh();
            dummyMesh.vertices = allFramesVertices[0];
            dummyMesh.triangles = allIndices.Select(i => (int)i).ToArray();
            dummyMesh.uv = allUVs;

            var compressed = CompressFromFrames(allFramesVertices, dummyMesh, allUVs, allColors);
            compressed.texture_filename = textureFilename ?? "";
            compressed.mesh_data_filename = meshDataFilename ?? "";
            return compressed;
        }

        /// <summary>
        /// Unified export pipeline: compress vertex animation with transforms baked into vertices, and save as RAT + ACT files.
        /// 
        /// CRITICAL FLOW:
        /// 1. Receives vertex frames (local space) + transform keyframes per frame
        /// 2. Applies transform matrix to EVERY vertex in EVERY frame
        ///    - Position: object-to-world translation
        ///    - Rotation: object-to-world rotation
        ///    - Scale: object-to-world scale
        /// 3. Resulting vertices are in WORLD SPACE with all animation baked in
        /// 4. Calls CompressFromFrames with transformed vertex data
        /// 5. CompressFromFrames calculates bounds from transformed data
        /// 6. Vertices quantized to 8-bit using these bounds
        /// 7. RAT file stores: bounds (float) + quantized vertices + delta stream
        /// 8. ACT file stores: mesh data + RAT file references (identity transforms only)
        /// 
        /// Result: RAT file is completely self-contained with all spatial data
        /// </summary>
        public static void ExportAnimation(
            string baseFilename,
            List<UnityEngine.Vector3[]> vertexFrames,
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] capturedUVs,
            UnityEngine.Color[] capturedColors,
            float framerate,
            string textureFilename = "",
            int maxFileSizeKB = 64,
            ActorRenderingMode renderingMode = ActorRenderingMode.TextureWithDirectionalLight,
            List<ActorTransformFloat> customTransforms = null,
            bool flipZ = true)
        {
            if (vertexFrames == null || vertexFrames.Count == 0)
            {
                UnityEngine.Debug.LogError("ExportAnimation: No vertex frames provided");
                return;
            }

            // STEP 1: Apply transforms to vertices
            // This converts from local space to world space with all animation baked in
            List<UnityEngine.Vector3[]> processedFrames = vertexFrames;
            if (customTransforms != null && customTransforms.Count == vertexFrames.Count)
            {
                UnityEngine.Debug.Log($"ExportAnimation: Baking {customTransforms.Count} transform frames into vertex animation...");
                processedFrames = new List<UnityEngine.Vector3[]>();
                
                for (int frameIndex = 0; frameIndex < vertexFrames.Count; frameIndex++)
                {
                    var transformData = customTransforms[frameIndex];
                    
                    // Build transform matrix: position, rotation, scale
                    UnityEngine.Matrix4x4 transformMatrix = UnityEngine.Matrix4x4.TRS(
                        transformData.position,
                        UnityEngine.Quaternion.Euler(transformData.rotation),
                        transformData.scale
                    );
                    
                    // Apply to all vertices in this frame
                    var transformedVertices = new UnityEngine.Vector3[vertexFrames[frameIndex].Length];
                    for (int vertexIndex = 0; vertexIndex < vertexFrames[frameIndex].Length; vertexIndex++)
                    {
                        var v = transformMatrix.MultiplyPoint3x4(vertexFrames[frameIndex][vertexIndex]);
                        transformedVertices[vertexIndex] = v;
                    }
                    
                    processedFrames.Add(transformedVertices);
                }
                UnityEngine.Debug.Log($"ExportAnimation: Transform baking complete - vertices now in world space{(flipZ ? " (Z-flipped)" : "")}");
            }
            else if (customTransforms == null)
            {
                UnityEngine.Debug.Log($"ExportAnimation: No transforms provided - vertices assumed to be in world space");
            }

            // STEP 2: Compress vertex animation
            // This will:
            // - Calculate bounds from transformed frames
            // - Quantize to 8-bit using bounds
            // - Store bounds in RAT header
            // If we flipped Z, triangle winding must be reversed to preserve face orientation
            UnityEngine.Mesh meshToUse = sourceMesh;
            if (flipZ && sourceMesh != null)
            {
                // Clone the mesh to avoid mutating the original
                meshToUse = new UnityEngine.Mesh();
                meshToUse.vertices = sourceMesh.vertices;
                meshToUse.uv = sourceMesh.uv;
                meshToUse.colors = sourceMesh.colors;
                var tris = sourceMesh.triangles;
                var reversed = new int[tris.Length];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    // invert winding
                    reversed[i] = tris[i];
                    reversed[i + 1] = tris[i + 2];
                    reversed[i + 2] = tris[i + 1];
                }
                meshToUse.triangles = reversed;
                meshToUse.RecalculateBounds();
            }

            // If we requested a Z-flip but frames are already in world space (no transforms), flip vertex coordinates here
            if (flipZ)
            {
                for (int f = 0; f < processedFrames.Count; f++)
                {
                    var frame = processedFrames[f];
                    for (int i = 0; i < frame.Length; i++)
                    {
                        var p = frame[i];
                        p.z = -p.z;
                        frame[i] = p;
                    }
                }
            }

            var compressed = CompressFromFrames(processedFrames, meshToUse, capturedUVs, capturedColors);
            if (compressed == null)
            {
                UnityEngine.Debug.LogError("ExportAnimation: Compression failed");
                return;
            }

            // Clean up texture filename - remove "assets/" prefix if present
            string cleanTextureFilename = textureFilename;
            if (!string.IsNullOrEmpty(cleanTextureFilename) && cleanTextureFilename.StartsWith("assets/"))
            {
                cleanTextureFilename = cleanTextureFilename.Substring("assets/".Length);
            }

            compressed.texture_filename = cleanTextureFilename;
            compressed.mesh_data_filename = $"{baseFilename}.ratmesh";

            // Create GeneratedData directory
            string generatedDataPath = System.IO.Path.Combine(UnityEngine.Application.dataPath.Replace("Assets", ""), "GeneratedData");
            if (!System.IO.Directory.Exists(generatedDataPath))
            {
                System.IO.Directory.CreateDirectory(generatedDataPath);
            }

            // STEP 3: Write RAT files
            // Contains: bounds (float) + bit widths + first frame + delta stream
            // ALL vertex animation is baked in, ready for direct rendering
            string baseFilePath = System.IO.Path.Combine(generatedDataPath, baseFilename);
            var ratFiles = WriteRatFileWithSizeSplitting(baseFilePath, compressed, maxFileSizeKB);

            // Validation step (editor/debug): read back the RAT and compare decompressed vertices to expected world-space positions
            try
            {
                if (ratFiles.Count > 0)
                {
                    string firstRat = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ratFiles[0]) ?? "", System.IO.Path.GetFileName(ratFiles[0]));
                    var ratAnim = Core.ReadRatFile(firstRat);
                    var ctx = Core.CreateDecompressionContext(ratAnim);
                    Core.DecompressToFrame(ctx, ratAnim, 0);
                    // Convert decompressed vertices to world-space floats
                    var decompressed = new UnityEngine.Vector3[ratAnim.num_vertices];
                    for (int i = 0; i < ratAnim.num_vertices; i++)
                    {
                        var v = ratAnim.first_frame[i];
                        float x = ratAnim.min_x + (v.x / 255f) * (ratAnim.max_x - ratAnim.min_x);
                        float y = ratAnim.min_y + (v.y / 255f) * (ratAnim.max_y - ratAnim.min_y);
                        float z = ratAnim.min_z + (v.z / 255f) * (ratAnim.max_z - ratAnim.min_z);
                        decompressed[i] = new UnityEngine.Vector3(x, y, z);
                    }
                    // Compare to expected from processedFrames[0]
                    float maxError = 0f;
                    var expected = processedFrames[0];
                    int count = Math.Min(expected.Length, decompressed.Length);
                    for (int i = 0; i < count; i++)
                    {
                        float e = UnityEngine.Vector3.Distance(expected[i], decompressed[i]);
                        maxError = Math.Max(maxError, e);
                    }
                    UnityEngine.Debug.Log($"ExportValidation: Decompressed first frame max error {maxError:F6} units (after quantization)");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"ExportValidation: Validation failed - {e.Message}");
            }

            // STEP 4: Create ACT file
            // Contains: mesh data (UVs, colors, indices) + RAT file references
            // Transform data is identity-only since all animation is in vertices
            var actorData = new ActorAnimationData();
            actorData.framerate = framerate;
            actorData.ratFilePaths.AddRange(ratFiles.ConvertAll(System.IO.Path.GetFileName));
            
            // Extract mesh data from source mesh
            actorData.meshUVs = capturedUVs ?? (meshToUse != null ? meshToUse.uv : sourceMesh.uv);
            actorData.meshColors = capturedColors ?? (meshToUse != null ? meshToUse.colors : sourceMesh.colors);
            actorData.meshIndices = (meshToUse != null ? meshToUse.triangles : sourceMesh.triangles);
            actorData.textureFilename = cleanTextureFilename;

            string actFilePath = System.IO.Path.Combine(generatedDataPath, $"{baseFilename}.act");
            Actor.SaveActorData(actFilePath, actorData, renderingMode, embedMeshData: true);
            
            UnityEngine.Debug.Log($"ExportAnimation: Complete");
            UnityEngine.Debug.Log($"  RAT files ({ratFiles.Count}): Bounds={compressed.min_x:F2}-{compressed.max_x:F2}, {compressed.min_y:F2}-{compressed.max_y:F2}, {compressed.min_z:F2}-{compressed.max_z:F2}");
            UnityEngine.Debug.Log($"  Vertices: {compressed.num_vertices}, Frames: {compressed.num_frames}");
            UnityEngine.Debug.Log($"  Texture: {cleanTextureFilename}");
            UnityEngine.Debug.Log($"  ACT file: Mesh data + RAT references (all transforms baked into RAT vertex data)");
        }
    }

    /// <summary>
    /// Material and rendering mode options for Actor rendering
    /// </summary>
    public enum ActorRenderingMode
    {
        VertexColoursOnly,
        VertexColoursWithDirectionalLight,
        VertexColoursWithVertexLighting,
        TextureOnly,
        TextureAndVertexColours,
        TextureWithDirectionalLight,
        TextureAndVertexColoursAndDirectionalLight,
        MatCap
    }

    /// <summary>
    /// Floating-point transform data used during recording (before compression)
    /// </summary>
    [System.Serializable]
    public struct ActorTransformFloat
    {
        public UnityEngine.Vector3 position;        // World position (represents model center)
        public UnityEngine.Vector3 rotation;        // World rotation (Euler angles in degrees)
        public UnityEngine.Vector3 scale;           // World scale
        public uint rat_file_index;     // Index into the RAT file list (0-based)
        public uint rat_local_frame;    // Frame index within the specific RAT file
    }
}
