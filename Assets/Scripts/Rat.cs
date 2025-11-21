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
    // Full quantized frames (for chunk-splitting validation and chunking)
    public VertexU8[][] quantized_frames;
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

                // Note: mesh_data_filename support removed — mesh data is now handled by .act files

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
            Array.Copy(anim.first_frame, ctx.current_positions, (int)anim.num_vertices);
            return ctx;
        }

        public static void DecompressToFrame(DecompressionContext ctx, CompressedAnimation anim, uint targetFrame)
        {
            if (targetFrame == ctx.current_frame) return;
            if (targetFrame >= anim.num_frames) targetFrame = anim.num_frames - 1;

            if (targetFrame < ctx.current_frame || ctx.current_frame == 0)
            {
                Array.Copy(anim.first_frame, ctx.current_positions, (int)anim.num_vertices);
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
        /// <summary>
        /// Computes the per-chunk metadata (list of chunk start frame and word counts) without writing files.
        /// Internal helper used by chunked writer.
        /// </summary>
        private class ChunkSpec
        {
            public uint startDeltaFrame;
            public uint chunkDeltaFrames;
            public uint startDeltaWord;
            public uint chunkDeltaWords;
            public string filename;
            public CompressedAnimation chunkAnim;
        }

        public static void WriteRatFileWithSizeSplittingChunked(
            string baseFilename,
            CompressedAnimation anim,
            int maxFileSizeKB = 64,
            List<UnityEngine.Vector3[]> processedFrames = null,
            System.Action<int,int> perChunkProgress = null,
            System.Action<List<string>> onComplete = null,
            bool skipValidation = false)
        {
#if UNITY_EDITOR
            // We'll compute chunk specs similarly to the synchronous method, but queue writes per chunk
            const int KB = 1024;
            int maxFileSize = maxFileSizeKB * KB;
            var createdFiles = new List<string>();

            // Calculate static data sizes for the .rat file (V3 format)
            uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeader));
            uint bitWidthsSize = anim.num_vertices * 3;
            uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));
            string meshDataFilename = !string.IsNullOrEmpty(anim.mesh_data_filename) ? anim.mesh_data_filename : $"{baseFilename}.ratmesh";
            byte[] meshDataFilenameBytes = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(meshDataFilename));
            uint rawFirstFrameSize = anim.isFirstFrameRaw ? anim.num_vertices * (uint)Marshal.SizeOf(typeof(UnityEngine.Vector3)) : 0;
            uint staticOverheadSize = headerSize + bitWidthsSize + firstFrameSize + (uint)meshDataFilenameBytes.Length + rawFirstFrameSize;

            if (staticOverheadSize > maxFileSize)
            {
                throw new System.InvalidOperationException($"ERROR: Static RAT data ({staticOverheadSize} bytes) exceeds maximum file size ({maxFileSize} bytes)!");
            }

            // if single frame or no deltas, just write synchronously
            if (anim.num_frames <= 1 || anim.delta_stream == null || anim.delta_stream.Length == 0)
            {
                string filename = $"{baseFilename}.rat";
                using (var stream = new FileStream(filename, FileMode.Create)) WriteRatFileV3(stream, anim, Path.GetFileName(meshDataFilename));
                createdFiles.Add(filename);
                onComplete?.Invoke(createdFiles);
                return;
            }

            // compute words per frame and prefixWords
            uint bitsPerFrame = 0;
            for (int i = 0; i < anim.num_vertices; i++) bitsPerFrame += (uint)(anim.bit_widths_x[i] + anim.bit_widths_y[i] + anim.bit_widths_z[i]);
            uint totalDeltaFramesToCompute = (anim.num_frames > 0) ? (anim.num_frames - 1) : 0;
            var wordsPerDeltaFrame = new uint[totalDeltaFramesToCompute > 0 ? totalDeltaFramesToCompute : 1];
            for (uint f = 0; f < totalDeltaFramesToCompute; ++f)
            {
                uint wordsUpToFPlus1 = (bitsPerFrame * (f + 1) + 31) / 32;
                uint wordsUpToF = (bitsPerFrame * f + 31) / 32;
                uint wordsThisFrame = wordsUpToFPlus1 - wordsUpToF;
                wordsPerDeltaFrame[f] = wordsThisFrame;
            }
            uint wordsPerFrame = (bitsPerFrame + 31) / 32;
            if (wordsPerFrame == 0) wordsPerFrame = 1;

            uint availableSpaceForDeltasFirst = (uint)(maxFileSize - staticOverheadSize);
            uint staticOverheadAppend = headerSize + bitWidthsSize + firstFrameSize + (uint)meshDataFilenameBytes.Length;
            uint availableSpaceForDeltasAppend = (uint)(maxFileSize - staticOverheadAppend);
            uint deltaStreamWordSize = sizeof(uint);
            uint totalDeltaWords = (uint)anim.delta_stream.Length;
            uint totalDeltaFrames = (anim.num_frames > 0) ? (anim.num_frames - 1) : 0;
            uint maxDeltaWordsPerChunk = availableSpaceForDeltasAppend / deltaStreamWordSize;
            if (maxDeltaWordsPerChunk == 0) maxDeltaWordsPerChunk = 1;

            uint deltaFramesPerAppendChunk = 0;
            if (totalDeltaFrames > 0)
            {
                uint acc = 0;
                for (uint i = 0; i < totalDeltaFrames; ++i)
                {
                    acc += wordsPerDeltaFrame[i];
                    if (acc >= maxDeltaWordsPerChunk)
                    {
                        deltaFramesPerAppendChunk = i + 1;
                        break;
                    }
                }
                if (deltaFramesPerAppendChunk == 0) deltaFramesPerAppendChunk = totalDeltaFrames;
            }
            else deltaFramesPerAppendChunk = 1;
            if (deltaFramesPerAppendChunk == 0) deltaFramesPerAppendChunk = 1;
            int numberOfChunks = UnityEngine.Mathf.CeilToInt((float)totalDeltaFrames / (float)deltaFramesPerAppendChunk);

            // Build prefix sums
            uint[] prefixWords = new uint[totalDeltaFrames + 1];
            prefixWords[0] = 0; uint running = 0;
            for (uint f = 0; f < totalDeltaFrames; ++f) { running += wordsPerDeltaFrame[f]; prefixWords[f + 1] = running; }

            var chunkSpecs = new List<ChunkSpec>();
            uint nextStartDfIndex = 0;
            for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
            {
                uint startDeltaFrame = nextStartDfIndex;
                uint remainingDeltaFrames = totalDeltaFrames - startDeltaFrame;
                uint chunkDeltaFrames = 0;
                if (chunkIndex == 0)
                {
                    uint deltaWordsFirst = availableSpaceForDeltasFirst / deltaStreamWordSize;
                    uint accWords = 0; uint framesFits = 0;
                    for (uint ff = startDeltaFrame; ff < startDeltaFrame + remainingDeltaFrames; ++ff)
                    {
                        if (ff >= totalDeltaFrames) break;
                        accWords += wordsPerDeltaFrame[ff];
                        if (accWords >= deltaWordsFirst) { framesFits = ff - startDeltaFrame + 1; break; }
                    }
                    if (framesFits == 0) framesFits = 1;
                    chunkDeltaFrames = System.Math.Min(remainingDeltaFrames, framesFits);
                }
                else
                {
                    chunkDeltaFrames = System.Math.Min(remainingDeltaFrames, deltaFramesPerAppendChunk);
                }

                uint startDeltaWord = prefixWords[startDeltaFrame];
                uint chunkDeltaWords = 0;
                for (uint f = startDeltaFrame; f < startDeltaFrame + chunkDeltaFrames; ++f)
                {
                    if (f < totalDeltaFrames) chunkDeltaWords += wordsPerDeltaFrame[f];
                }

                var spec = new ChunkSpec() { startDeltaFrame = startDeltaFrame, chunkDeltaFrames = chunkDeltaFrames, startDeltaWord = startDeltaWord, chunkDeltaWords = chunkDeltaWords };
                chunkSpecs.Add(spec);
                nextStartDfIndex += chunkDeltaFrames;
            }

            // Build chunk objects and filenames
            for (int ci = 0; ci < chunkSpecs.Count; ++ci)
            {
                var spec = chunkSpecs[ci];
                var chunkAnim = new CompressedAnimation
                {
                    num_vertices = anim.num_vertices,
                    num_indices = anim.num_indices,
                    num_frames = (ci == 0) ? anim.num_frames : (spec.chunkDeltaFrames + 1),
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

                long startLong = Math.Min((long)spec.startDeltaWord, (long)totalDeltaWords);
                int totalDeltaWordsInt = (int)totalDeltaWords;
                if (spec.chunkDeltaWords > 0 && anim.delta_stream != null && anim.delta_stream.Length > 0)
                {
                    if (startLong >= totalDeltaWordsInt)
                    {
                        chunkAnim.delta_stream = new uint[0];
                    }
                    else
                    {
                        int available = totalDeltaWordsInt - (int)startLong;
                        int copyWords = (int)Math.Min((long)spec.chunkDeltaWords, (long)available);
                        chunkAnim.delta_stream = new uint[copyWords];
                        System.Array.Copy(anim.delta_stream, (int)startLong, chunkAnim.delta_stream, 0, copyWords);
                    }
                }
                else
                {
                    chunkAnim.delta_stream = new uint[0];
                }

                if (ci == 0)
                {
                    // Keep first frame intact
                }
                else
                {
                    if (anim.quantized_frames != null && anim.quantized_frames.Length > spec.startDeltaFrame)
                    {
                        chunkAnim.first_frame = anim.quantized_frames[spec.startDeltaFrame];
                    }
                    else
                    {
                        var temp = new VertexU8[chunkAnim.num_vertices];
                        Array.Copy(anim.first_frame, temp, (int)anim.num_vertices);
                        if (spec.startDeltaFrame > 0 && anim.delta_stream != null && anim.delta_stream.Length > 0)
                        {
                            var reader = new BitstreamReader(anim.delta_stream);
                            for (uint f = 0; f < spec.startDeltaFrame; ++f)
                            {
                                for (int v = 0; v < temp.Length; ++v)
                                {
                                    int bx = anim.bit_widths_x[v];
                                    int by = anim.bit_widths_y[v];
                                    int bz = anim.bit_widths_z[v];
                                    int rawx = (int)reader.Read(bx);
                                    int dx = SignExtendLocal((uint)rawx, bx);
                                    int rawy = (int)reader.Read(by);
                                    int dy = SignExtendLocal((uint)rawy, by);
                                    int rawz = (int)reader.Read(bz);
                                    int dz = SignExtendLocal((uint)rawz, bz);
                                    temp[v].x = (byte)(temp[v].x + dx);
                                    temp[v].y = (byte)(temp[v].y + dy);
                                    temp[v].z = (byte)(temp[v].z + dz);
                                }
                            }
                        }
                        chunkAnim.first_frame = temp;
                    }
                    chunkAnim.isFirstFrameRaw = false;
                    chunkAnim.first_frame_raw = null;
                    chunkAnim.mesh_data_filename = string.Empty;
                }

                string filename = (chunkSpecs.Count == 1) ? $"{baseFilename}.rat" : $"{baseFilename}_part{ci + 1:D2}of{chunkSpecs.Count:D2}.rat";
                spec.filename = filename;
                spec.chunkAnim = chunkAnim;
            }

            // Now write per chunk via EditorApplication.update
            var created = new List<string>();
            int totalChunks = chunkSpecs.Count;
            int currentChunk = 0;
            UnityEditor.EditorApplication.CallbackFunction updater = null;
            updater = () =>
            {
                if (currentChunk >= totalChunks)
                {
                    UnityEditor.EditorApplication.update -= updater;
                    onComplete?.Invoke(created);
                    return;
                }
                try
                {
                    var spec = chunkSpecs[currentChunk];
                    using (var stream = new FileStream(spec.filename, FileMode.Create)) WriteRatFileV3(stream, spec.chunkAnim, spec.chunkAnim.mesh_data_filename);
                    created.Add(spec.filename);
                    perChunkProgress?.Invoke(currentChunk + 1, totalChunks);
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError($"WriteRatFileWithSizeSplittingChunked: failed to write chunk {currentChunk + 1}: {e.Message}");
                    UnityEditor.EditorApplication.update -= updater;
                    onComplete?.Invoke(created);
                    return;
                }
                currentChunk++;
            };
            // register update handler
            UnityEditor.EditorApplication.update += updater;
#else
            // Non-editor fallback: synchronous write
            var files = WriteRatFileWithSizeSplitting(baseFilename, anim, maxFileSizeKB, processedFrames, perChunkProgress);
            onComplete?.Invoke(files);
#endif
        }
        // Enable/disable optional heavy per-chunk validation (decompressing multiple frames per chunk).
        // Disabled by default to avoid blocking the Editor during normal saves.
        public static bool enableHeavyValidation = false;
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

        // Sign extend helper for Tool to reuse the same logic as Core.SignExtend
        private static int SignExtendLocal(uint value, int bits)
        {
            if (bits == 0) return 0;
            int shift = 32 - bits;
            return (int)((value << shift) >> shift);
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
    public static List<string> WriteRatFileWithSizeSplitting(string baseFilename, CompressedAnimation anim, int maxFileSizeKB = 64, List<UnityEngine.Vector3[]> processedFrames = null, System.Action<int,int> perChunkProgress = null)
        {
            const int KB = 1024;
            int maxFileSize = maxFileSizeKB * KB;
            var createdFiles = new List<string>();

            // Note: Mesh data (UVs, colors, indices) is embedded in .act files (version 5)
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
            
            // Calculate bits per frame and words per frame (per-frame words may vary due to bit packing)
            uint bitsPerFrame = 0;
            for (int i = 0; i < anim.num_vertices; i++) bitsPerFrame += (uint)(anim.bit_widths_x[i] + anim.bit_widths_y[i] + anim.bit_widths_z[i]);
            // Precompute words used per delta-frame to account for bit packing across frames
            uint totalDeltaFramesToCompute = (anim.num_frames > 0) ? (anim.num_frames - 1) : 0;
            var wordsPerDeltaFrame = new uint[totalDeltaFramesToCompute > 0 ? totalDeltaFramesToCompute : 1];
            uint cumulativeWordsBefore = 0;
            for (uint f = 0; f < totalDeltaFramesToCompute; ++f)
            {
                // Words used up to and including this frame
                uint wordsUpToFPlus1 = (bitsPerFrame * (f + 1) + 31) / 32;
                uint wordsUpToF = (bitsPerFrame * f + 31) / 32;
                uint wordsThisFrame = wordsUpToFPlus1 - wordsUpToF;
                wordsPerDeltaFrame[f] = wordsThisFrame;
                cumulativeWordsBefore += wordsThisFrame;
            }
            // total words per frame average fallback
            uint wordsPerFrame = (bitsPerFrame + 31) / 32;

            // Calculate available space for delta data per chunk (first chunk may include raw first frame)
            uint availableSpaceForDeltasFirst = (uint)(maxFileSize - staticOverheadSize);
            // For subsequent chunks: omit raw_first_frame (if present) and mesh filename to maximize delta space if desired
            uint staticOverheadAppend = headerSize + bitWidthsSize + firstFrameSize + (uint)meshDataFilenameBytes.Length; // exclude rawFirstFrame if present
            uint availableSpaceForDeltasAppend = (uint)(maxFileSize - staticOverheadAppend);
            uint deltaStreamWordSize = sizeof(uint);
            
            // Calculate how to split the delta stream
            uint totalDeltaWords = (uint)anim.delta_stream.Length;
            uint totalDeltaFrames = (anim.num_frames > 0) ? (anim.num_frames - 1) : 0;
            if (wordsPerFrame == 0) wordsPerFrame = 1; // avoid div by zero for degenerate cases
            // Use append available space as baseline for frames-per-chunk (since most chunks will be append-like)
            uint maxDeltaWordsPerChunk = availableSpaceForDeltasAppend / deltaStreamWordSize;
            
            if (maxDeltaWordsPerChunk == 0)
            {
                throw new System.InvalidOperationException(
                    $"ERROR: Cannot fit any delta data within {maxFileSizeKB}KB limit! Static overhead is {staticOverheadSize} bytes, " +
                    $"leaving only {availableSpaceForDeltasAppend} bytes for deltas (append-case), but need at least {deltaStreamWordSize} bytes per delta word.");
            }
            
            // If maxDeltaWordsPerChunk is zero, ensure at least one word per chunk
            if (maxDeltaWordsPerChunk == 0) maxDeltaWordsPerChunk = 1;
            // Use the precomputed per-frame word counts to figure the best frame count that fits in a chunk
            // We'll derive chunking by summing wordsPerDeltaFrame per-frame values until we reach maxDeltaWordsPerChunk
            uint deltaFramesPerAppendChunk = 0;
            if (totalDeltaFrames > 0)
            {
                uint acc = 0;
                for (uint i = 0; i < totalDeltaFrames; ++i)
                {
                    acc += wordsPerDeltaFrame[i];
                    if (acc >= maxDeltaWordsPerChunk)
                    {
                        deltaFramesPerAppendChunk = i + 1;
                        break;
                    }
                }
                if (deltaFramesPerAppendChunk == 0)
                {
                    // If we didn't reach the threshold, use all remaining frames
                    deltaFramesPerAppendChunk = totalDeltaFrames;
                }
            }
            else
            {
                deltaFramesPerAppendChunk = 1;
            }
            if (deltaFramesPerAppendChunk == 0) deltaFramesPerAppendChunk = 1; // ensure at least one delta frame per chunk
            int numberOfChunks = UnityEngine.Mathf.CeilToInt((float)totalDeltaFrames / (float)deltaFramesPerAppendChunk);
            
            UnityEngine.Debug.Log($"Splitting RAT animation into {numberOfChunks} chunks (max {maxFileSizeKB}KB each):");
            
            uint nextStartDfIndex = 0;
            // Precompute prefix word sums for fast start offsets
            uint[] prefixWords = new uint[totalDeltaFrames + 1]; // prefixWords[frame] = words used by frames [0 .. frame-1]
            uint running = 0;
            prefixWords[0] = 0;
            for (uint f = 0; f < totalDeltaFrames; ++f)
            {
                running += wordsPerDeltaFrame[f];
                prefixWords[f + 1] = running;
            }
            for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
            {
                perChunkProgress?.Invoke(chunkIndex + 1, numberOfChunks);
                uint startDeltaFrame = nextStartDfIndex; // zero-based delta frame index
                uint remainingDeltaFrames = totalDeltaFrames - startDeltaFrame;
                uint chunkDeltaFrames = 0;
                if (chunkIndex == 0)
                {
                    uint deltaWordsFirst = availableSpaceForDeltasFirst / deltaStreamWordSize;
                    // Determine how many frames fit by summing per-frame words
                    uint accWords = 0;
                    uint framesFits = 0;
                    for (uint ff = startDeltaFrame; ff < startDeltaFrame + remainingDeltaFrames; ++ff)
                    {
                        if (ff >= totalDeltaFrames) break;
                        accWords += wordsPerDeltaFrame[ff];
                        if (accWords > deltaWordsFirst) break;
                        framesFits++;
                    }
                    if (framesFits == 0) framesFits = 1;
                    chunkDeltaFrames = (uint)System.Math.Min(remainingDeltaFrames, framesFits);
                }
                else
                {
                    chunkDeltaFrames = (uint)System.Math.Min(remainingDeltaFrames, deltaFramesPerAppendChunk);
                }
                // chunk covers frames: base frame index = startDeltaFrame, dest frames = startDeltaFrame+1 .. startDeltaFrame + chunkDeltaFrames
                // start and lengths in words using exact per-frame word counts
                uint startDeltaWord = prefixWords[startDeltaFrame];
                // Sum per-frame words for this chunk
                uint chunkDeltaWords = 0;
                for (uint f = startDeltaFrame; f < startDeltaFrame + chunkDeltaFrames; ++f)
                {
                    if (f < totalDeltaFrames) chunkDeltaWords += wordsPerDeltaFrame[f];
                }
                
                    var chunkAnim = new CompressedAnimation
                {
                    num_vertices = anim.num_vertices,
                    num_indices = anim.num_indices,
                        // num_frames: for first chunk we write global animation frame count; for appended chunks write local chunk length
                            num_frames = (chunkIndex == 0) ? anim.num_frames : (chunkDeltaFrames + 1),
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
                
                if (chunkDeltaWords > 0 && anim.delta_stream != null && anim.delta_stream.Length > 0)
                {
                    int totalDeltaWordsInt = anim.delta_stream.Length;
                    long startLong = Math.Min((long)startDeltaWord, (long)totalDeltaWordsInt);
                    if (startLong >= totalDeltaWordsInt)
                    {
                        UnityEngine.Debug.LogWarning($"RAT: computed startDeltaWord {startDeltaWord} >= totalDeltaWords {totalDeltaWordsInt}, writing empty delta stream for chunk.");
                        chunkAnim.delta_stream = new uint[0];
                    }
                    else
                    {
                        int available = totalDeltaWordsInt - (int)startLong;
                        int copyWords = (int)Math.Min((long)chunkDeltaWords, (long)available);
                        if (copyWords < chunkDeltaWords)
                        {
                            UnityEngine.Debug.LogWarning($"RAT: chunk copy length clamped from {chunkDeltaWords} to {copyWords} due to available source words ({available}).");
                        }
                        chunkAnim.delta_stream = new uint[copyWords];
                        System.Array.Copy(anim.delta_stream, (int)startLong, chunkAnim.delta_stream, 0, copyWords);
                        // Update chunkDeltaWords to reflect actual copied words
                        chunkDeltaWords = (uint)copyWords;
                        // Recompute actual chunkDeltaFrames in case the copied words were less than expected
                        uint actualChunkDeltaFrames = 0;
                        uint wordsConsumedCheck = 0;
                        for (uint ff = startDeltaFrame; ff < startDeltaFrame + chunkDeltaFrames && ff < totalDeltaFrames; ++ff)
                        {
                            uint w = wordsPerDeltaFrame[ff];
                            if (wordsConsumedCheck + w > chunkDeltaWords) break;
                            wordsConsumedCheck += w;
                            actualChunkDeltaFrames++;
                        }
                        chunkDeltaFrames = actualChunkDeltaFrames;
                    }
                }
                else
                {
                    chunkAnim.delta_stream = new uint[0];
                }
                
                string filename = (numberOfChunks == 1)
                    ? $"{baseFilename}.rat"
                    : $"{baseFilename}_part{chunkIndex + 1:D2}of{numberOfChunks:D2}.rat";
                
                // Prepare chunk-specific metadata
                // For non-first chunks, set the chunk header first_frame to the quantized frame at chunk start
                if (chunkIndex == 0)
                {
                    // First chunk keeps anim.first_frame and optionally raw_first_frame and mesh filename
                }
                else
                {
                    // Try to seed from precomputed quantized frames if available (most accurate)
                    if (anim.quantized_frames != null && anim.quantized_frames.Length > startDeltaFrame)
                    {
                        chunkAnim.first_frame = anim.quantized_frames[startDeltaFrame];
                    }
                    else
                    {
                        // Fallback: reconstruct by applying deltas from global first frame up to startDeltaFrame
                        var temp = new VertexU8[chunkAnim.num_vertices];
                        Array.Copy(anim.first_frame, temp, (int)anim.num_vertices);
                        if (startDeltaFrame > 0 && anim.delta_stream != null && anim.delta_stream.Length > 0)
                        {
                            // Read deltas from the global stream up to startDeltaFrame
                            var reader = new BitstreamReader(anim.delta_stream);
                            for (uint f = 0; f < startDeltaFrame; ++f)
                            {
                                for (int v = 0; v < temp.Length; ++v)
                                {
                                    int bx = anim.bit_widths_x[v];
                                    int by = anim.bit_widths_y[v];
                                    int bz = anim.bit_widths_z[v];
                                    int rawx = (int)reader.Read(bx);
                                    int dx = SignExtendLocal((uint)rawx, bx);
                                    int rawy = (int)reader.Read(by);
                                    int dy = SignExtendLocal((uint)rawy, by);
                                    int rawz = (int)reader.Read(bz);
                                    int dz = SignExtendLocal((uint)rawz, bz);
                                    temp[v].x = (byte)(temp[v].x + dx);
                                    temp[v].y = (byte)(temp[v].y + dy);
                                    temp[v].z = (byte)(temp[v].z + dz);
                                }
                            }
                        }
                        chunkAnim.first_frame = temp;
                    }
                    // Do not include raw_first_frame or mesh filename on appended chunks to save space
                    chunkAnim.isFirstFrameRaw = false;
                    chunkAnim.first_frame_raw = null;
                    chunkAnim.mesh_data_filename = string.Empty;
                }

                // Ensure num_frames header reflects actual copied chunk frames
                if (chunkIndex != 0)
                {
                    chunkAnim.num_frames = (chunkDeltaFrames + 1);
                }
                using (var stream = new FileStream(filename, FileMode.Create))
                {
                    WriteRatFileV3(stream, chunkAnim, chunkAnim.mesh_data_filename);
                }
                
                createdFiles.Add(filename);
                UnityEngine.Debug.Log($"  Chunk {chunkIndex + 1}: {filename} ({new FileInfo(filename).Length} bytes)");
                
                // Optional: Validate written chunk by reading it back and comparing decompressed frames
                if (processedFrames != null && anim.quantized_frames != null)
                {
                    try
                    {
                        // To validate the chunk, create a temporary anim seeded with the quantized frame at startDeltaFrame
                        var validationAnim = new CompressedAnimation
                        {
                            num_vertices = chunkAnim.num_vertices,
                            num_frames = chunkAnim.num_frames,
                            num_indices = chunkAnim.num_indices,
                            min_x = chunkAnim.min_x, max_x = chunkAnim.max_x,
                            min_y = chunkAnim.min_y, max_y = chunkAnim.max_y,
                            min_z = chunkAnim.min_z, max_z = chunkAnim.max_z,
                            bit_widths_x = chunkAnim.bit_widths_x,
                            bit_widths_y = chunkAnim.bit_widths_y,
                            bit_widths_z = chunkAnim.bit_widths_z,
                            delta_stream = chunkAnim.delta_stream,
                            first_frame = new VertexU8[chunkAnim.num_vertices]
                        };
                        // Seed validationAnim.first_frame from global quantized frames at the chunk start
                        if (chunkAnim.first_frame != null && chunkAnim.first_frame.Length > 0)
                        {
                            var src = chunkAnim.first_frame;
                            int srcLen = src.Length;
                            int dstLen = validationAnim.first_frame.Length;
                            int copyLen = Math.Min((int)chunkAnim.num_vertices, Math.Min(srcLen, dstLen));
                            Array.Copy(src, validationAnim.first_frame, copyLen);
                        }
                        else if (anim.quantized_frames != null && anim.quantized_frames.Length > startDeltaFrame)
                        {
                            var src = anim.quantized_frames[startDeltaFrame];
                            int srcLen = src.Length;
                            int dstLen = validationAnim.first_frame.Length;
                            int copyLen = Math.Min((int)chunkAnim.num_vertices, Math.Min(srcLen, dstLen));
                            Array.Copy(src, validationAnim.first_frame, copyLen);
                        }
                        var ctxChunk = Core.CreateDecompressionContext(validationAnim);
                        uint baseFrameIndex = startDeltaFrame; // base frame index in global processedFrames
                        float chunkMaxError = 0f;
                        int chunkMaxErrorVertexIndex = -1;
                        UnityEngine.Vector3 chunkMaxExpected = UnityEngine.Vector3.zero;
                        UnityEngine.Vector3 chunkMaxActual = UnityEngine.Vector3.zero;
                        // Check only the first frame (decompressing all frames can block the editor on large animations)
                        uint maxValidateFrames = (enableHeavyValidation? validationAnim.num_frames : 1u);
                        for (uint local = 0; local < maxValidateFrames && local < validationAnim.num_frames; ++local)
                        {
                            Core.DecompressToFrame(ctxChunk, validationAnim, local);
                            // Compare each vertex
                            int compareFrameIndex = (int)(baseFrameIndex + local);
                            if (compareFrameIndex >= processedFrames.Count) break; // outside of provided frames
                            var expectedFrame = processedFrames[compareFrameIndex];
                            for (int v = 0; v < validationAnim.num_vertices; ++v)
                            {
                                var q = ctxChunk.current_positions[v];
                                float x = validationAnim.min_x + (q.x / 255f) * (validationAnim.max_x - validationAnim.min_x);
                                float y = validationAnim.min_y + (q.y / 255f) * (validationAnim.max_y - validationAnim.min_y);
                                float z = validationAnim.min_z + (q.z / 255f) * (validationAnim.max_z - validationAnim.min_z);
                                var epos = expectedFrame[v];
                                float err = UnityEngine.Vector3.Distance(new UnityEngine.Vector3(x, y, z), epos);
                                if (err > chunkMaxError) {
                                    chunkMaxError = err;
                                    chunkMaxErrorVertexIndex = v;
                                    chunkMaxExpected = epos;
                                    chunkMaxActual = new UnityEngine.Vector3(x, y, z);
                                }
                            }
                        }
                        // Compute a dynamic validation tolerance based on the quantization step size
                        float stepX = (validationAnim.max_x - validationAnim.min_x) / 255f;
                        float stepY = (validationAnim.max_y - validationAnim.min_y) / 255f;
                        float stepZ = (validationAnim.max_z - validationAnim.min_z) / 255f;
                        // Worst-case Euclidean error for a single-axis rounding is sqrt((step/2)^2*3).
                        float quantizationTolerance = UnityEngine.Mathf.Sqrt((stepX * 0.5f) * (stepX * 0.5f) + (stepY * 0.5f) * (stepY * 0.5f) + (stepZ * 0.5f) * (stepZ * 0.5f));
                        // Add a small safety margin and a conservative minimum tolerance
                        float validationTolerance = UnityEngine.Mathf.Max(quantizationTolerance * 1.2f, 0.01f);
                        UnityEngine.Debug.Log($"RAT Validation: tolerance={validationTolerance:F6} (stepX={stepX:F6} stepY={stepY:F6} stepZ={stepZ:F6})");
                        if (chunkMaxError > validationTolerance)
                        {
                            UnityEngine.Debug.LogError($"RAT Validation FAILED for chunk {chunkIndex + 1}: max error {chunkMaxError:F6} exceeds tolerance {validationTolerance:F6}. startDeltaFrame={startDeltaFrame}, chunkFrames={chunkDeltaFrames}");
                            if (chunkMaxErrorVertexIndex >= 0)
                            {
                                UnityEngine.Debug.LogError($"Validation fail: vertexIndex={chunkMaxErrorVertexIndex}, expected={chunkMaxExpected}, actual={chunkMaxActual}, diff={(chunkMaxActual - chunkMaxExpected)}");
                            }
                            try
                            {
                                if (validationAnim.first_frame != null && validationAnim.first_frame.Length > 0)
                                {
                                    var qf = validationAnim.first_frame[0];
                                    var globalF = (anim.quantized_frames != null && anim.quantized_frames.Length > startDeltaFrame) ? anim.quantized_frames[startDeltaFrame][0] : validationAnim.first_frame[0];
                                    UnityEngine.Debug.LogError($"Chunk seed sample: chunk.first_frame[0] = ({qf.x},{qf.y},{qf.z}), global.first_frame_at_start = ({globalF.x},{globalF.y},{globalF.z})");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                UnityEngine.Debug.LogWarning($"Validation debug: failed to print first_frame sample: {ex.Message}");
                            }
                        }
                        else
                        {
                            UnityEngine.Debug.Log($"RAT Validation OK for chunk {chunkIndex + 1}: max error {chunkMaxError:F6} (tolerance {validationTolerance:F6}). startDeltaFrame={startDeltaFrame}, chunkFrames={chunkDeltaFrames}");
                        }
                    }
                    catch (System.Exception e)
                    {
                        UnityEngine.Debug.LogWarning($"ExportValidation: Failed to validate chunk {chunkIndex + 1}: {e.Message}");
                    }
                }
                // Advance the start index for the next chunk
                nextStartDfIndex += chunkDeltaFrames;
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
    /// Note: this expects frames with transforms already applied (world-space vertices).
    /// Bounds are computed from the input frames and stored in the RAT header.
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

            Array.Copy(quantizedFrames[0], anim.first_frame, (int)numVertices);

            // Keep quantized frames around for writer chunking and validation
            anim.quantized_frames = quantizedFrames;

            // Keep quantized frames around for writer chunking and validation
            anim.quantized_frames = quantizedFrames;

            if (preserveFirstFrame)
            {
                anim.first_frame_raw = new UnityEngine.Vector3[numVertices];
                Array.Copy(rawFrames[0], anim.first_frame_raw, (int)numVertices);
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
        /// Compress animation data with explicit bounds (optimized for particle systems).
        /// This version uses provided bounds instead of calculating from all vertices,
        /// allowing exclusion of inactive/collapsed particles from bounds calculation.
        /// </summary>
        public static CompressedAnimation CompressFromFramesWithBounds(
            List<UnityEngine.Vector3[]> rawFrames,
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] staticUVs,
            UnityEngine.Color[] staticColors,
            UnityEngine.Vector3 minBounds,
            UnityEngine.Vector3 maxBounds,
            bool preserveFirstFrame = false)
        {
            if (rawFrames == null || rawFrames.Count == 0) return null;
            if (!ValidateMeshForRAT(sourceMesh)) return null;

            uint numVertices = (uint)sourceMesh.vertexCount;
            uint numFrames = (uint)rawFrames.Count;
            uint numIndices = (uint)sourceMesh.triangles.Length;

            var sourceIndices = sourceMesh.triangles;
            
            UnityEngine.Debug.Log($"Compression using explicit bounds: Min({minBounds.x:F3}, {minBounds.y:F3}, {minBounds.z:F3}) Max({maxBounds.x:F3}, {maxBounds.y:F3}, {maxBounds.z:F3})");

            // Quantize ALL frames to 8-bit using the provided bounds
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
                    quantizedFrames[f][v].x = (byte)UnityEngine.Mathf.RoundToInt(255 * UnityEngine.Mathf.Clamp01((rawFrames[f][v].x - minBounds.x) / range.x));
                    quantizedFrames[f][v].y = (byte)UnityEngine.Mathf.RoundToInt(255 * UnityEngine.Mathf.Clamp01((rawFrames[f][v].y - minBounds.y) / range.y));
                    quantizedFrames[f][v].z = (byte)UnityEngine.Mathf.RoundToInt(255 * UnityEngine.Mathf.Clamp01((rawFrames[f][v].z - minBounds.z) / range.z));
                }
            }

            // Handle UVs, Colors, and Indices
            var uvs = new VertexUV[numVertices];
            var colors = new VertexColor[numVertices];

            if (staticUVs != null && staticUVs.Length == numVertices)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    uvs[v].u = staticUVs[v].x;
                    uvs[v].v = staticUVs[v].y;
                }
            }
            else
            {
                var sourceUVs = sourceMesh.uv;
                for (int v = 0; v < numVertices; v++)
                {
                    if (v < sourceUVs.Length)
                    {
                        uvs[v].u = sourceUVs[v].x;
                        uvs[v].v = sourceUVs[v].y;
                    }
                    else
                    {
                        uvs[v].u = 0;
                        uvs[v].v = 0;
                    }
                }
            }

            if (staticColors != null && staticColors.Length == numVertices)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    colors[v].r = staticColors[v].r;
                    colors[v].g = staticColors[v].g;
                    colors[v].b = staticColors[v].b;
                    colors[v].a = staticColors[v].a;
                }
            }
            else
            {
                var sourceColors = sourceMesh.colors;
                for (int v = 0; v < numVertices; v++)
                {
                    if (v < sourceColors.Length)
                    {
                        colors[v].r = sourceColors[v].r;
                        colors[v].g = sourceColors[v].g;
                        colors[v].b = sourceColors[v].b;
                        colors[v].a = sourceColors[v].a;
                    }
                    else
                    {
                        colors[v].r = 1;
                        colors[v].g = 1;
                        colors[v].b = 1;
                        colors[v].a = 1;
                    }
                }
            }

            var indices = new ushort[numIndices];
            for (int i = 0; i < numIndices; i++)
            {
                indices[i] = (ushort)sourceIndices[i];
            }

            // Calculate per-vertex bit widths for delta encoding
            var bitWidthsX = new byte[numVertices];
            var bitWidthsY = new byte[numVertices];
            var bitWidthsZ = new byte[numVertices];

            for (int v = 0; v < numVertices; v++)
            {
                byte calculatedBitsX = 0, calculatedBitsY = 0, calculatedBitsZ = 0;
                for (int f = 1; f < numFrames; f++)
                {
                    int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                    int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                    int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;

                    byte bitsX = BitsForDelta(dx);
                    byte bitsY = BitsForDelta(dy);
                    byte bitsZ = BitsForDelta(dz);

                    if (bitsX > calculatedBitsX) calculatedBitsX = bitsX;
                    if (bitsY > calculatedBitsY) calculatedBitsY = bitsY;
                    if (bitsZ > calculatedBitsZ) calculatedBitsZ = bitsZ;
                }
                
                // No max bits constraints in this overload - use calculated values directly
                bitWidthsX[v] = calculatedBitsX;
                bitWidthsY[v] = calculatedBitsY;
                bitWidthsZ[v] = calculatedBitsZ;
            }

            // Create compressed animation
            var anim = new CompressedAnimation
            {
                num_vertices = numVertices,
                num_frames = numFrames,
                num_indices = numIndices,
                uvs = uvs,
                colors = colors,
                indices = indices,
                first_frame = quantizedFrames[0],
                quantized_frames = quantizedFrames,
                isFirstFrameRaw = preserveFirstFrame,
                min_x = minBounds.x,
                min_y = minBounds.y,
                min_z = minBounds.z,
                max_x = maxBounds.x,
                max_y = maxBounds.y,
                max_z = maxBounds.z,
                bit_widths_x = bitWidthsX,
                bit_widths_y = bitWidthsY,
                bit_widths_z = bitWidthsZ
            };

            if (preserveFirstFrame)
            {
                anim.first_frame_raw = rawFrames[0];
            }

            if (numFrames > 1)
            {
                var bitstream = new BitstreamWriter();
                for (int f = 1; f < numFrames; f++)
                {
                    for (int v = 0; v < numVertices; v++)
                    {
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;

                        uint ux = (uint)dx;
                        uint uy = (uint)dy;
                        uint uz = (uint)dz;

                        bitstream.Write(ux, bitWidthsX[v]);
                        bitstream.Write(uy, bitWidthsY[v]);
                        bitstream.Write(uz, bitWidthsZ[v]);
                    }
                }
                bitstream.Flush();
                anim.delta_stream = bitstream.ToArray();
            }
            else
            {
                anim.delta_stream = Array.Empty<uint>();
            }
            
            UnityEngine.Debug.Log($"Compressed with explicit bounds: range X({range.x:F3}) Y({range.y:F3}) Z({range.z:F3})");
            
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

            if (staticUVs != null && staticUVs.Length == numVertices)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    uvs[v].u = staticUVs[v].x;
                    uvs[v].v = staticUVs[v].y;
                }
            }
            else
            {
                var sourceUVs = sourceMesh.uv;
                for (int v = 0; v < numVertices; v++)
                {
                    if (v < sourceUVs.Length)
                    {
                        uvs[v].u = sourceUVs[v].x;
                        uvs[v].v = sourceUVs[v].y;
                    }
                    else
                    {
                        uvs[v].u = 0;
                        uvs[v].v = 0;
                    }
                }
            }

            if (staticColors != null && staticColors.Length == numVertices)
            {
                for (int v = 0; v < numVertices; v++)
                {
                    colors[v].r = staticColors[v].r;
                    colors[v].g = staticColors[v].g;
                    colors[v].b = staticColors[v].b;
                    colors[v].a = staticColors[v].a;
                }
            }
            else
            {
                var sourceColors = sourceMesh.colors;
                for (int v = 0; v < numVertices; v++)
                {
                    if (v < sourceColors.Length)
                    {
                        colors[v].r = sourceColors[v].r;
                        colors[v].g = sourceColors[v].g;
                        colors[v].b = sourceColors[v].b;
                        colors[v].a = sourceColors[v].a;
                    }
                    else
                    {
                        colors[v].r = 1;
                        colors[v].g = 1;
                        colors[v].b = 1;
                        colors[v].a = 1;
                    }
                }
            }

            var indices = new ushort[numIndices];
            for (int i = 0; i < numIndices; i++)
            {
                indices[i] = (ushort)sourceIndices[i];
            }

            // Calculate per-vertex bit widths for delta encoding
            var bitWidthsX = new byte[numVertices];
            var bitWidthsY = new byte[numVertices];
            var bitWidthsZ = new byte[numVertices];

            for (int v = 0; v < numVertices; v++)
            {
                byte calculatedBitsX = 0, calculatedBitsY = 0, calculatedBitsZ = 0;
                for (int f = 1; f < numFrames; f++)
                {
                    int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                    int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                    int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;

                    byte bitsX = BitsForDelta(dx);
                    byte bitsY = BitsForDelta(dy);
                    byte bitsZ = BitsForDelta(dz);

                    if (bitsX > calculatedBitsX) calculatedBitsX = bitsX;
                    if (bitsY > calculatedBitsY) calculatedBitsY = bitsY;
                    if (bitsZ > calculatedBitsZ) calculatedBitsZ = bitsZ;
                }
                
                // No max bits constraints in this overload - use calculated values directly
                bitWidthsX[v] = calculatedBitsX;
                bitWidthsY[v] = calculatedBitsY;
                bitWidthsZ[v] = calculatedBitsZ;
            }

            // Create compressed animation
            var anim = new CompressedAnimation
            {
                num_vertices = numVertices,
                num_frames = numFrames,
                num_indices = numIndices,
                uvs = uvs,
                colors = colors,
                indices = indices,
                first_frame = quantizedFrames[0],
                quantized_frames = quantizedFrames,
                isFirstFrameRaw = preserveFirstFrame,
                min_x = minBounds.x,
                min_y = minBounds.y,
                min_z = minBounds.z,
                max_x = maxBounds.x,
                max_y = maxBounds.y,
                max_z = maxBounds.z,
                bit_widths_x = bitWidthsX,
                bit_widths_y = bitWidthsY,
                bit_widths_z = bitWidthsZ
            };

            if (preserveFirstFrame)
            {
                anim.first_frame_raw = rawFrames[0];
            }

            if (numFrames > 1)
            {
                var bitstream = new BitstreamWriter();
                for (int f = 1; f < numFrames; f++)
                {
                    for (int v = 0; v < numVertices; v++)
                    {
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;

                        uint ux = (uint)dx;
                        uint uy = (uint)dy;
                        uint uz = (uint)dz;

                        bitstream.Write(ux, bitWidthsX[v]);
                        bitstream.Write(uy, bitWidthsY[v]);
                        bitstream.Write(uz, bitWidthsZ[v]);
                    }
                }
                bitstream.Flush();
                anim.delta_stream = bitstream.ToArray();
            }
            else
            {
                anim.delta_stream = Array.Empty<uint>();
            }
            
            UnityEngine.Debug.Log($"Final stored bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            
            return anim;
        }

        /// <summary>
        /// Unified export pipeline: compress vertex animation with transforms baked into vertices, and save as RAT + ACT files.
        /// 
    /// Flow: apply transforms to frames, compress vertex deltas, write RAT/ACT files.
    /// - Transforms are applied to vertices to produce world-space frames
    /// - Compression uses computed bounds and 8-bit quantization per axis
    /// - RAT files contain bounds, quantized vertices, and delta streams
    /// - ACT files contain mesh data and RAT references (no per-frame transforms)
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
            bool flipZ = true,
            bool skipValidation = false,
            bool yieldPerChunk = false,
            System.Action<List<string>> onComplete = null)
        {
            if (vertexFrames == null || vertexFrames.Count == 0)
            {
                UnityEngine.Debug.LogError("ExportAnimation: No vertex frames provided");
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.DisplayProgressBar("Exporting", "Preparing animation...", 0.05f);
#endif

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
#if UNITY_EDITOR
                UnityEditor.EditorUtility.ClearProgressBar();
#endif
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
            // Show progress and run chunk-writing; this may be expensive so keep the user informed
#if UNITY_EDITOR
            UnityEditor.EditorUtility.DisplayProgressBar("Exporting", "Writing RAT files...", 0.5f);
#endif
            // Local helper to complete validation and ACT creation after RAT files are written (used by chunked writer)
            void CompleteExportAfterRATs(string baseFilePathInner, CompressedAnimation compressedInner, List<UnityEngine.Vector3[]> processedFramesInner, bool skipValidationInner, List<string> ratFilesInner)
            {
                // Validation step (editor/debug): read back the RAT and compare decompressed vertices to expected world-space positions
                try
                {
                    if (!skipValidationInner && ratFilesInner.Count > 0)
                    {
                        string firstRat = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ratFilesInner[0]) ?? "", System.IO.Path.GetFileName(ratFilesInner[0]));
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
                        // Compare to expected from processedFramesInner[0]
                        float maxError = 0f;
                        var expected = processedFramesInner[0];
                        int count = System.Math.Min(expected.Length, decompressed.Length);
                        for (int i = 0; i < count; i++)
                        {
                            float e = UnityEngine.Vector3.Distance(expected[i], decompressed[i]);
                            maxError = System.Math.Max(maxError, e);
                        }
                        UnityEngine.Debug.Log($"ExportValidation: Decompressed first frame max error {maxError:F6} units (after quantization)");
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"ExportValidation: Validation failed - {e.Message}");
                }
                finally
                {
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.ClearProgressBar();
#endif
                }

                // STEP 4: Create ACT file - contains mesh + RAT references
                var actorDataInner = new ActorAnimationData();
                actorDataInner.framerate = framerate;
                actorDataInner.ratFilePaths.AddRange(ratFilesInner.ConvertAll(System.IO.Path.GetFileName));
                actorDataInner.meshUVs = capturedUVs ?? (meshToUse != null ? meshToUse.uv : sourceMesh.uv);
                actorDataInner.meshColors = capturedColors ?? (meshToUse != null ? meshToUse.colors : sourceMesh.colors);
                actorDataInner.meshIndices = (meshToUse != null ? meshToUse.triangles : sourceMesh.triangles);
                actorDataInner.textureFilename = cleanTextureFilename;
                string actFilePathInner = System.IO.Path.Combine(generatedDataPath, $"{baseFilename}.act");
                Actor.SaveActorData(actFilePathInner, actorDataInner, renderingMode, embedMeshData: true);
            }

            if (yieldPerChunk)
            {
                // Use the chunked writer; perform validation/act creation in the completion callback
                WriteRatFileWithSizeSplittingChunked(baseFilePath, compressed, maxFileSizeKB, skipValidation ? null : processedFrames,
                    (done, total) => {
#if UNITY_EDITOR
                        float progress = 0.5f + 0.4f * (done / (float)System.Math.Max(1, total));
                        UnityEditor.EditorUtility.DisplayProgressBar("Exporting", $"Writing RAT files... ({done}/{total})", progress);
                        #endif
                    },
                    (fileList) => {
                        // Continue with validation and ACT creation on the main thread after chunks are written
                        try
                        {
                            CompleteExportAfterRATs(baseFilePath, compressed, processedFrames, skipValidation, fileList);
                            onComplete?.Invoke(fileList);
                        }
                        catch (System.Exception e)
                        {
                            UnityEngine.Debug.LogError($"ExportAnimation: error during post-chunk completion: {e.Message}\n{e}");
                        }
                        finally
                        {
                            #if UNITY_EDITOR
                            UnityEditor.EditorUtility.ClearProgressBar();
                            #endif
                        }
                    });
                return;
            }
#if UNITY_EDITOR
            var ratFiles = WriteRatFileWithSizeSplitting(baseFilePath, compressed, maxFileSizeKB, skipValidation ? null : processedFrames,
                (done, total) => {
                    float progress = 0.5f + 0.4f * (done / (float)System.Math.Max(1, total));
                    UnityEditor.EditorUtility.DisplayProgressBar("Exporting", $"Writing RAT files... ({done}/{total})", progress);
                });
#else
            var ratFiles = WriteRatFileWithSizeSplitting(baseFilePath, compressed, maxFileSizeKB, skipValidation ? null : processedFrames);
#endif

            // Validation step (editor/debug): read back the RAT and compare decompressed vertices to expected world-space positions
            try
            {
                if (!skipValidation && ratFiles.Count > 0)
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
            finally
            {
#if UNITY_EDITOR
                UnityEditor.EditorUtility.ClearProgressBar();
#endif
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

        /// <summary>
        /// Unified export pipeline with maximum bit width constraint for delta compression.
        /// </summary>
        public static void ExportAnimationWithMaxBits(
            string baseFilename,
            List<UnityEngine.Vector3[]> vertexFrames,
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] capturedUVs,
            UnityEngine.Color[] capturedColors,
            float framerate,
            string textureFilename = "",
            int maxFileSizeKB = 64,
            ActorRenderingMode renderingMode = ActorRenderingMode.TextureWithDirectionalLight,
            int maxBitsPerAxis = 8,
            List<ActorTransformFloat> customTransforms = null,
            bool flipZ = true,
            bool skipValidation = false,
            bool yieldPerChunk = false,
            System.Action<List<string>> onComplete = null)
        {
            if (vertexFrames == null || vertexFrames.Count == 0)
            {
                UnityEngine.Debug.LogError("ExportAnimationWithMaxBits: No vertex frames provided");
                onComplete?.Invoke(new List<string>());
                return;
            }

            // STEP 1: Apply transforms to vertices (if provided)
            List<UnityEngine.Vector3[]> processedFrames = vertexFrames;
            if (customTransforms != null && customTransforms.Count == vertexFrames.Count)
            {
                UnityEngine.Debug.Log($"ExportAnimationWithMaxBits: Baking {customTransforms.Count} transform frames into vertex animation...");
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
            }

            // Handle Z-flipping
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

            if (flipZ)
            {
                // If processedFrames is still referencing vertexFrames, we need to clone it to avoid modifying original data
                if (processedFrames == vertexFrames)
                {
                    processedFrames = new List<UnityEngine.Vector3[]>(vertexFrames.Count);
                    foreach (var frame in vertexFrames)
                    {
                        processedFrames.Add((UnityEngine.Vector3[])frame.Clone());
                    }
                }

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

            // Compress with bit width constraints
            var compressed = CompressFromFrames(processedFrames, meshToUse, capturedUVs, capturedColors, false, maxBitsPerAxis, maxBitsPerAxis, maxBitsPerAxis);
            if (compressed == null)
            {
                UnityEngine.Debug.LogError("ExportAnimationWithMaxBits: Compression failed");
                onComplete?.Invoke(new List<string>());
                return;
            }

            compressed.texture_filename = textureFilename;
            compressed.mesh_data_filename = $"{baseFilename}.ratmesh";

            string generatedDataPath = System.IO.Path.Combine(UnityEngine.Application.dataPath.Replace("Assets", ""), "GeneratedData");
            if (!System.IO.Directory.Exists(generatedDataPath))
            {
                System.IO.Directory.CreateDirectory(generatedDataPath);
            }

            string baseFilePath = System.IO.Path.Combine(generatedDataPath, baseFilename);

            if (yieldPerChunk)
            {
                WriteRatFileWithSizeSplittingChunked(baseFilePath, compressed, maxFileSizeKB, processedFrames, null, onComplete);
            }
            else
            {
                var ratFiles = WriteRatFileWithSizeSplitting(baseFilePath, compressed, maxFileSizeKB, processedFrames);
                onComplete?.Invoke(ratFiles);
            }
        }
    }

    // ActorRenderingMode enum moved into Rat namespace to be available as Rat.ActorRenderingMode.

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
