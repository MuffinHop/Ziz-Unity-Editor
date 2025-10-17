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
        public uint magic;
        public uint num_vertices;
        public uint num_frames;
        public uint num_indices;
        public uint uv_offset;
        public uint color_offset;
        public uint indices_offset;
        public uint delta_offset;
        public uint bit_widths_offset; // Offset to bit widths array
        public float min_x, min_y, min_z;
        public float max_x, max_y, max_z;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RatHeaderV2
    {
        public uint magic;              // "RAT2" = 0x32544152
        public uint num_vertices;
        public uint num_frames;
        public uint num_indices;
        public uint uv_offset;
        public uint color_offset;
        public uint indices_offset;
        public uint delta_offset;
        public uint bit_widths_offset; // Offset to bit widths array
        public uint texture_filename_offset; // Offset to texture filename data
        public uint texture_filename_length; // Length of texture filename string
        public float min_x, min_y, min_z;
        public float max_x, max_y, max_z;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] reserved;         // Additional reserved space for V2
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
        public string texture_filename = ""; // V2: Texture filename for this animation
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

        public static CompressedAnimation ReadRatFile(Stream stream)
        {
            using var reader = new BinaryReader(stream);

            // Read magic to determine format version
            uint magic = reader.ReadUInt32();
            stream.Seek(0, SeekOrigin.Begin); // Reset to beginning

            if (magic == 0x32544152) // "RAT2"
            {
                return ReadRatFileV2(stream);
            }
            else if (magic == 0x31544152) // "RAT1"
            {
                return ReadRatFileV1(stream);
            }
            else
            {
                throw new InvalidDataException($"Invalid RAT file magic: 0x{magic:X8}");
            }
        }

        /// <summary>
        /// Read RAT file in V1 format (legacy compatibility)
        /// </summary>
        public static CompressedAnimation ReadRatFileV1(Stream stream)
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
                min_z = header.min_z, max_z = header.max_z,
                texture_filename = "" // V1 format doesn't have texture filename
            };

            reader.BaseStream.Seek(header.uv_offset, SeekOrigin.Begin);
            anim.uvs = new VertexUV[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++)
            {
                anim.uvs[i] = new VertexUV { u = reader.ReadSingle(), v = reader.ReadSingle() };
            }

            reader.BaseStream.Seek(header.color_offset, SeekOrigin.Begin);
            anim.colors = new VertexColor[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++)
            {
                anim.colors[i] = new VertexColor { r = reader.ReadSingle(), g = reader.ReadSingle(), b = reader.ReadSingle(), a = reader.ReadSingle() };
            }

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

        /// <summary>
        /// Read RAT file in V2 format (with texture filename support)
        /// </summary>
        public static CompressedAnimation ReadRatFileV2(Stream stream)
        {
            using var reader = new BinaryReader(stream);
            
            byte[] headerBytes = reader.ReadBytes(Marshal.SizeOf(typeof(RatHeaderV2)));
            IntPtr ptr = Marshal.AllocHGlobal(headerBytes.Length);
            Marshal.Copy(headerBytes, 0, ptr, headerBytes.Length);
            RatHeaderV2 header = Marshal.PtrToStructure<RatHeaderV2>(ptr);
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

            // Read UV coordinates
            reader.BaseStream.Seek(header.uv_offset, SeekOrigin.Begin);
            anim.uvs = new VertexUV[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++)
            {
                anim.uvs[i] = new VertexUV { u = reader.ReadSingle(), v = reader.ReadSingle() };
            }

            // Read vertex colors
            reader.BaseStream.Seek(header.color_offset, SeekOrigin.Begin);
            anim.colors = new VertexColor[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++)
            {
                anim.colors[i] = new VertexColor { r = reader.ReadSingle(), g = reader.ReadSingle(), b = reader.ReadSingle(), a = reader.ReadSingle() };
            }

            // Read indices
            reader.BaseStream.Seek(header.indices_offset, SeekOrigin.Begin);
            anim.indices = new ushort[anim.num_indices];
            for (int i = 0; i < anim.num_indices; i++) anim.indices[i] = reader.ReadUInt16();

            // Read bit widths
            reader.BaseStream.Seek(header.bit_widths_offset, SeekOrigin.Begin);
            anim.bit_widths_x = reader.ReadBytes((int)anim.num_vertices);
            anim.bit_widths_y = reader.ReadBytes((int)anim.num_vertices);
            anim.bit_widths_z = reader.ReadBytes((int)anim.num_vertices);

            // Read first frame
            long firstFrameOffset = header.bit_widths_offset + (anim.num_vertices * 3);
            reader.BaseStream.Seek(firstFrameOffset, SeekOrigin.Begin);
            anim.first_frame = new VertexU8[anim.num_vertices];
            for (int i = 0; i < anim.num_vertices; i++) anim.first_frame[i] = new VertexU8 { x = reader.ReadByte(), y = reader.ReadByte(), z = reader.ReadByte() };

            // Read texture filename
            reader.BaseStream.Seek(header.texture_filename_offset, SeekOrigin.Begin);
            if (header.texture_filename_length > 0)
            {
                byte[] textureFilenameBytes = reader.ReadBytes((int)header.texture_filename_length);
                anim.texture_filename = System.Text.Encoding.UTF8.GetString(textureFilenameBytes);
            }
            else
            {
                anim.texture_filename = "";
            }

            // Read delta stream
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

        public static void WriteRatFile(Stream stream, CompressedAnimation anim)
        {
            // Use V2 format if texture filename is provided, otherwise V1
            if (!string.IsNullOrEmpty(anim.texture_filename))
            {
                WriteRatFileV2(stream, anim);
            }
            else
            {
                WriteRatFileV1(stream, anim);
            }
        }

        /// <summary>
        /// Write RAT file in V1 format (legacy compatibility)
        /// </summary>
        public static void WriteRatFileV1(Stream stream, CompressedAnimation anim)
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
                delta_offset = headerSize + uvSize + colorSize + indicesSize + bitWidthsSize + firstFrameSize,
                bit_widths_offset = headerSize + uvSize + colorSize + indicesSize,
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
            foreach (var index in anim.indices) writer.Write(index); // Write all indices, even if empty array
            
            writer.Write(anim.bit_widths_x);
            writer.Write(anim.bit_widths_y);
            writer.Write(anim.bit_widths_z);

            foreach (var v in anim.first_frame) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            foreach (var word in anim.delta_stream) writer.Write(word); // Write all delta stream words, even if empty
        }

        /// <summary>
        /// Write RAT file in V2 format (with texture filename support)
        /// </summary>
        public static void WriteRatFileV2(Stream stream, CompressedAnimation anim)
        {
            using var writer = new BinaryWriter(stream);

            uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeaderV2));
            uint uvSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexUV));
            uint colorSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexColor));
            uint indicesSize = anim.num_indices * sizeof(ushort);
            uint bitWidthsSize = anim.num_vertices * 3;
            uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));
            
            // Texture filename data (UTF-8 encoded)
            byte[] textureFilenameBytes = System.Text.Encoding.UTF8.GetBytes(anim.texture_filename ?? "");
            uint textureFilenameSize = (uint)textureFilenameBytes.Length;

            var header = new RatHeaderV2
            {
                magic = 0x32544152, // "RAT2"
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
                texture_filename_offset = headerSize + uvSize + colorSize + indicesSize + bitWidthsSize + firstFrameSize,
                texture_filename_length = textureFilenameSize,
                delta_offset = headerSize + uvSize + colorSize + indicesSize + bitWidthsSize + firstFrameSize + textureFilenameSize,
                reserved = new byte[8]
            };

            // Write header
            byte[] headerBytes = new byte[headerSize];
            IntPtr ptr = Marshal.AllocHGlobal((int)headerSize);
            Marshal.StructureToPtr(header, ptr, false);
            Marshal.Copy(ptr, headerBytes, 0, (int)headerSize);
            Marshal.FreeHGlobal(ptr);
            writer.Write(headerBytes);

            // Write data sections
            foreach (var uv in anim.uvs) { writer.Write(uv.u); writer.Write(uv.v); }
            foreach (var color in anim.colors) { writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a); }
            foreach (var index in anim.indices) writer.Write(index);
            
            writer.Write(anim.bit_widths_x);
            writer.Write(anim.bit_widths_y);
            writer.Write(anim.bit_widths_z);

            foreach (var v in anim.first_frame) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            
            // Write texture filename
            writer.Write(textureFilenameBytes);
            
            // Write delta stream
            foreach (var word in anim.delta_stream) writer.Write(word);
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
            
            // Calculate static data sizes (shared across all chunks)
            uint headerSize = (uint)Marshal.SizeOf(typeof(RatHeader));
            uint uvSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexUV));
            uint colorSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexColor));
            uint indicesSize = anim.num_indices * sizeof(ushort);
            uint bitWidthsSize = anim.num_vertices * 3;
            uint firstFrameSize = anim.num_vertices * (uint)Marshal.SizeOf(typeof(VertexU8));
            
            // Calculate static overhead (everything except delta stream)
            uint staticOverheadSize = headerSize + uvSize + colorSize + indicesSize + bitWidthsSize + firstFrameSize;
            
            // Check if the first frame alone exceeds the size limit
            if (staticOverheadSize > maxFileSize)
            {
                throw new System.InvalidOperationException(
                    $"ERROR: Static RAT data ({staticOverheadSize} bytes) exceeds maximum file size ({maxFileSize} bytes)!\n" +
                    $"Breakdown: Header({headerSize}) + UVs({uvSize}) + Colors({colorSize}) + Indices({indicesSize}) + BitWidths({bitWidthsSize}) + FirstFrame({firstFrameSize})\n" +
                    $"Consider reducing mesh complexity or increasing maxFileSizeKB parameter.");
            }
            
            // Calculate available space for delta data per chunk
            uint availableSpaceForDeltas = (uint)(maxFileSize - staticOverheadSize);
            uint deltaStreamWordSize = sizeof(uint); // Each delta stream entry is a uint (4 bytes)
            
            // If single frame or no delta data, create single file
            if (anim.num_frames <= 1 || anim.delta_stream == null || anim.delta_stream.Length == 0)
            {
                string filename = $"{baseFilename}.rat";
                using (var stream = new FileStream(filename, FileMode.Create))
                {
                    WriteRatFile(stream, anim);
                }
                createdFiles.Add(filename);
                UnityEngine.Debug.Log($"Created single RAT file: {filename} ({staticOverheadSize} bytes)");
                return createdFiles;
            }
            
            // Calculate how to split the delta stream
            uint totalDeltaWords = (uint)anim.delta_stream.Length;
            uint maxDeltaWordsPerChunk = availableSpaceForDeltas / deltaStreamWordSize;
            
            if (maxDeltaWordsPerChunk == 0)
            {
                throw new System.InvalidOperationException(
                    $"ERROR: Cannot fit any delta data within {maxFileSizeKB}KB limit! Static overhead is {staticOverheadSize} bytes, " +
                    $"leaving only {availableSpaceForDeltas} bytes for deltas, but need at least {deltaStreamWordSize} bytes per delta word.");
            }
            
            // Calculate number of chunks needed
            int numberOfChunks = UnityEngine.Mathf.CeilToInt((float)totalDeltaWords / maxDeltaWordsPerChunk);
            
            UnityEngine.Debug.Log($"Splitting RAT animation into {numberOfChunks} chunks (max {maxFileSizeKB}KB each):");
            UnityEngine.Debug.Log($"  Static overhead: {staticOverheadSize} bytes");
            UnityEngine.Debug.Log($"  Total delta words: {totalDeltaWords} ({totalDeltaWords * deltaStreamWordSize} bytes)");
            UnityEngine.Debug.Log($"  Max delta words per chunk: {maxDeltaWordsPerChunk}");
            
            // Create chunks
            for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
            {
                uint startDeltaWord = (uint)(chunkIndex * maxDeltaWordsPerChunk);
                uint endDeltaWord = System.Math.Min(startDeltaWord + maxDeltaWordsPerChunk, totalDeltaWords);
                uint chunkDeltaWords = endDeltaWord - startDeltaWord;
                
                // Create chunk animation data
                var chunkAnim = new CompressedAnimation
                {
                    num_vertices = anim.num_vertices,
                    num_indices = anim.num_indices,
                    num_frames = chunkDeltaWords > 0 ? (uint)UnityEngine.Mathf.CeilToInt((float)chunkDeltaWords / anim.num_vertices) : 1,
                    min_x = anim.min_x, max_x = anim.max_x,
                    min_y = anim.min_y, max_y = anim.max_y,
                    min_z = anim.min_z, max_z = anim.max_z,
                    uvs = anim.uvs,
                    colors = anim.colors,
                    indices = anim.indices,
                    first_frame = anim.first_frame,
                    bit_widths_x = anim.bit_widths_x,
                    bit_widths_y = anim.bit_widths_y,
                    bit_widths_z = anim.bit_widths_z
                };
                
                // Extract delta stream chunk
                if (chunkDeltaWords > 0)
                {
                    chunkAnim.delta_stream = new uint[chunkDeltaWords];
                    System.Array.Copy(anim.delta_stream, startDeltaWord, chunkAnim.delta_stream, 0, chunkDeltaWords);
                }
                else
                {
                    chunkAnim.delta_stream = new uint[0];
                }
                
                // Generate filename
                string filename;
                if (numberOfChunks == 1)
                {
                    filename = $"{baseFilename}.rat";
                }
                else
                {
                    filename = $"{baseFilename}_part{chunkIndex + 1:D2}of{numberOfChunks:D2}.rat";
                }
                
                // Write chunk file
                using (var stream = new FileStream(filename, FileMode.Create))
                {
                    WriteRatFile(stream, chunkAnim);
                }
                
                long actualFileSize = new FileInfo(filename).Length;
                createdFiles.Add(filename);
                
                UnityEngine.Debug.Log($"  Chunk {chunkIndex + 1}: {filename} ({actualFileSize} bytes, {chunkAnim.num_frames} frames)");
                
                // Verify chunk size doesn't exceed limit
                if (actualFileSize > maxFileSize)
                {
                    UnityEngine.Debug.LogWarning($"WARNING: Chunk file {filename} ({actualFileSize} bytes) exceeds target size ({maxFileSize} bytes)");
                }
            }
            
            UnityEngine.Debug.Log($"RAT splitting complete! Created {numberOfChunks} files totaling {totalDeltaWords * deltaStreamWordSize + staticOverheadSize * numberOfChunks} bytes");
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

        public static CompressedAnimation CompressFromFrames(List<UnityEngine.Vector3[]> rawFrames, UnityEngine.Mesh sourceMesh)
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

            // 3. Get UVs, Colors, and Indices from source mesh
            var uvs = new VertexUV[numVertices];
            var colors = new VertexColor[numVertices];
            var indices = new ushort[numIndices];
            var sourceUVs = sourceMesh.uv;
            var sourceColors = sourceMesh.colors;
            // sourceIndices already declared earlier for validation

            for (int i = 0; i < numVertices; i++) 
            {
                // Convert UVs from 0.0-1.0 float to 0-255 byte
                if(sourceUVs.Length > i) 
                {
                    uvs[i] = new VertexUV { 
                        u = sourceUVs[i].x,
                        v = sourceUVs[i].y
                    };
                }
                
                // Use colors directly as floats
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
                    // Default white color if no source colors
                    colors[i] = new VertexColor { r = 255, g = 255, b = 255, a = 255 };
                }
            }
            for (int i = 0; i < numIndices; i++) indices[i] = (ushort)sourceIndices[i];

            // --- Call Core Compression Logic ---
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
                bit_widths_z = new byte[numVertices]
            };

            Array.Copy(quantizedFrames[0], anim.first_frame, numVertices);

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
            
            // Debug: Log final bounds that will be stored in the file
            UnityEngine.Debug.Log($"Final stored bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            
            return anim;
        }
        
        /// <summary>
        /// Compress animation data from frames with optional per-frame UV and color data.
        /// </summary>
        /// <param name="rawFrames">Vertex position data per frame</param>
        /// <param name="sourceMesh">Source mesh for fallback UV/color data and indices</param>
        /// <param name="perFrameUVs">Optional per-frame UV data (null for static UVs)</param>
        /// <param name="perFrameColors">Optional per-frame color data (null for static colors)</param>
        /// <returns>Compressed animation data</returns>
        public static CompressedAnimation CompressFromFrames(
            List<UnityEngine.Vector3[]> rawFrames, 
            UnityEngine.Mesh sourceMesh,
            List<UnityEngine.Vector2[]> perFrameUVs = null,
            List<UnityEngine.Color[]> perFrameColors = null)
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

            // 3. Handle UVs - use per-frame data if available, otherwise fall back to source mesh
            var uvs = new VertexUV[numVertices];
            if (perFrameUVs != null && perFrameUVs.Count > 0)
            {
                // Use UVs from the first frame as the base (could be extended to support animated UVs in the future)
                var firstFrameUVs = perFrameUVs[0];
                for (int i = 0; i < numVertices && i < firstFrameUVs.Length; i++)
                {
                    // Use UVs directly as floats
                    uvs[i] = new VertexUV { 
                        u = firstFrameUVs[i].x,
                        v = firstFrameUVs[i].y
                    };
                }
                UnityEngine.Debug.Log($"Using per-frame UVs from captured data (using first frame)");
            }
            else
            {
                // Fall back to source mesh UVs
                var sourceUVs = sourceMesh.uv;
                for (int i = 0; i < numVertices; i++)
                {
                    if (sourceUVs.Length > i)
                    {
                        // Use UVs directly as floats
                        uvs[i] = new VertexUV { 
                            u = sourceUVs[i].x,
                            v = sourceUVs[i].y
                        };
                    }
                }
                UnityEngine.Debug.Log($"Using static UVs from source mesh");
            }

            // 4. Handle Colors - use per-frame data if available, otherwise fall back to source mesh
            var colors = new VertexColor[numVertices];
            if (perFrameColors != null && perFrameColors.Count > 0)
            {
                // Use colors from the first frame as the base (could be extended to support animated colors in the future)
                var firstFrameColors = perFrameColors[0];
                for (int i = 0; i < numVertices && i < firstFrameColors.Length; i++)
                {
                    // Use colors directly as floats
                    colors[i] = new VertexColor { 
                        r = firstFrameColors[i].r,
                        g = firstFrameColors[i].g,
                        b = firstFrameColors[i].b,
                        a = firstFrameColors[i].a
                    };
                }
                UnityEngine.Debug.Log($"Using per-frame colors from captured data (using first frame)");
            }
            else
            {
                // Fall back to source mesh colors
                var sourceColors = sourceMesh.colors;
                for (int i = 0; i < numVertices; i++)
                {
                    if (sourceColors.Length > i)
                    {
                        // Use colors directly as floats
                        colors[i] = new VertexColor { 
                            r = sourceColors[i].r,
                            g = sourceColors[i].g,
                            b = sourceColors[i].b,
                            a = sourceColors[i].a
                        };
                    }
                    else
                    {
                        // Default white color if no source colors
                        colors[i] = new VertexColor { r = 1.0f, g = 1.0f, b = 1.0f, a = 1.0f };
                    }
                }
                UnityEngine.Debug.Log($"Using static colors from source mesh");
            }

            // 5. Get indices from source mesh
            var indices = new ushort[numIndices];
            // sourceIndices already declared earlier for validation
            for (int i = 0; i < numIndices; i++) indices[i] = (ushort)sourceIndices[i];

            // 6. Create the compressed animation object
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
                bit_widths_z = new byte[numVertices]
            };

            Array.Copy(quantizedFrames[0], anim.first_frame, numVertices);

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
            
            // Debug: Log final bounds that will be stored in the file
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
        /// <returns>Compressed animation data</returns>
        public static CompressedAnimation CompressFromFrames(
            List<UnityEngine.Vector3[]> rawFrames, 
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] staticUVs = null,
            UnityEngine.Color[] staticColors = null)
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

            // 3. Handle UVs - use static data if available, otherwise fall back to source mesh
            var uvs = new VertexUV[numVertices];
            if (staticUVs != null && staticUVs.Length > 0)
            {
                for (int i = 0; i < numVertices && i < staticUVs.Length; i++)
                {
                    // Use UVs directly as floats
                    uvs[i] = new VertexUV { 
                        u = staticUVs[i].x,
                        v = staticUVs[i].y
                    };
                }
                UnityEngine.Debug.Log($"Using static UVs from captured data ({staticUVs.Length} UVs)");
            }
            else
            {
                // Fall back to source mesh UVs
                var sourceUVs = sourceMesh.uv;
                for (int i = 0; i < numVertices; i++)
                {
                    if (sourceUVs.Length > i)
                    {
                        // Use UVs directly as floats
                        uvs[i] = new VertexUV { 
                            u = sourceUVs[i].x,
                            v = sourceUVs[i].y
                        };
                    }
                }
                UnityEngine.Debug.Log($"Using static UVs from source mesh ({sourceUVs.Length} UVs)");
            }

            // 4. Handle Colors - use static data if available, otherwise fall back to source mesh
            var colors = new VertexColor[numVertices];
            if (staticColors != null && staticColors.Length > 0)
            {
                for (int i = 0; i < numVertices && i < staticColors.Length; i++)
                {
                    // Use colors directly as floats
                    colors[i] = new VertexColor { 
                        r = staticColors[i].r,
                        g = staticColors[i].g,
                        b = staticColors[i].b,
                        a = staticColors[i].a
                    };
                }
                UnityEngine.Debug.Log($"Using static colors from captured data ({staticColors.Length} colors)");
            }
            else
            {
                // Fall back to source mesh colors
                var sourceColors = sourceMesh.colors;
                for (int i = 0; i < numVertices; i++)
                {
                    if (sourceColors.Length > i)
                    {
                        // Use colors directly as floats
                        colors[i] = new VertexColor { 
                            r = sourceColors[i].r,
                            g = sourceColors[i].g,
                            b = sourceColors[i].b,
                            a = sourceColors[i].a
                        };
                    }
                    else
                    {
                        // Default white color if no source colors
                        colors[i] = new VertexColor { r = 1.0f, g = 1.0f, b = 1.0f, a = 1.0f };
                    }
                }
                UnityEngine.Debug.Log($"Using static colors from source mesh ({sourceColors.Length} colors)");
            }

            // 5. Get indices from source mesh
            var indices = new ushort[numIndices];
            // sourceIndices already declared earlier for validation
            for (int i = 0; i < numIndices; i++) indices[i] = (ushort)sourceIndices[i];

            // 6. Create the compressed animation object
            var anim = new CompressedAnimation
            {
                num_vertices = numVertices,
                num_frames = numFrames,
                num_indices = numIndices,
                uvs = uvs,
                colors = colors,
                indices = indices,
                min_x = minBounds.x,
                min_y = minBounds.y,
                min_z = minBounds.z,
                max_x = maxBounds.x,
                max_y = maxBounds.y,
                max_z = maxBounds.z,
                first_frame = new VertexU8[numVertices],
                bit_widths_x = new byte[numVertices],
                bit_widths_y = new byte[numVertices],
                bit_widths_z = new byte[numVertices]
            };

            // 7. Set the first frame
            Array.Copy(quantizedFrames[0], anim.first_frame, numVertices);

            // 8. Compress delta frames if there are multiple frames
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
            
            // Debug: Log final bounds that will be stored in the file
            UnityEngine.Debug.Log($"Final stored bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            
            return anim;
        }
        
        /// <summary>
        /// Compress animation data from frames with static UV/color data and optional bit width limits for improved compression.
        /// </summary>
        /// <param name="rawFrames">Vertex position data per frame</param>
        /// <param name="sourceMesh">Source mesh for fallback UV/color data and indices</param>
        /// <param name="staticUVs">Static UV data (null for source mesh UVs)</param>
        /// <param name="staticColors">Static color data (null for source mesh colors)</param>
        /// <param name="maxBitsX">Maximum bits for X deltas (1-8). Deltas exceeding this will be clamped.</param>
        /// <param name="maxBitsY">Maximum bits for Y deltas (1-8). Deltas exceeding this will be clamped.</param>
        /// <param name="maxBitsZ">Maximum bits for Z deltas (1-8). Deltas exceeding this will be clamped.</param>
        /// <returns>Compressed animation data with controlled precision</returns>
        public static CompressedAnimation CompressFromFrames(
            List<UnityEngine.Vector3[]> rawFrames, 
            UnityEngine.Mesh sourceMesh,
            UnityEngine.Vector2[] staticUVs,
            UnityEngine.Color[] staticColors,
            int maxBitsX,
            int maxBitsY,
            int maxBitsZ)
        {
            if (rawFrames == null || rawFrames.Count == 0) return null;
            if (!ValidateMeshForRAT(sourceMesh)) return null;

            uint numVertices = (uint)sourceMesh.vertexCount;
            uint numFrames = (uint)rawFrames.Count;
            uint numIndices = (uint)sourceMesh.triangles.Length;

            // Get triangle indices for use throughout the method
            var sourceIndices = sourceMesh.triangles;

            // Clamp bit limits to valid range
            maxBitsX = UnityEngine.Mathf.Clamp(maxBitsX, 1, 8);
            maxBitsY = UnityEngine.Mathf.Clamp(maxBitsY, 1, 8);
            maxBitsZ = UnityEngine.Mathf.Clamp(maxBitsZ, 1, 8);

            // Calculate maximum representable delta for each axis
            int maxDeltaX = (1 << (maxBitsX - 1)) - 1; // e.g., 4 bits -> max delta ±7
            int maxDeltaY = (1 << (maxBitsY - 1)) - 1;
            int maxDeltaZ = (1 << (maxBitsZ - 1)) - 1;

            UnityEngine.Debug.Log($"Compression limits: maxBits X={maxBitsX} (±{maxDeltaX}), Y={maxBitsY} (±{maxDeltaY}), Z={maxBitsZ} (±{maxDeltaZ})");

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

            // 3. Handle UVs
            var uvs = new VertexUV[numVertices];
            if (staticUVs != null)
            {
                for (int i = 0; i < numVertices && i < staticUVs.Length; i++)
                {
                    uvs[i] = new VertexUV { 
                        u = staticUVs[i].x,
                        v = staticUVs[i].y
                    };
                }
                UnityEngine.Debug.Log($"Using provided static UVs");
            }
            else
            {
                var sourceUVs = sourceMesh.uv;
                for (int i = 0; i < numVertices; i++)
                {
                    if (sourceUVs.Length > i)
                    {
                        uvs[i] = new VertexUV { 
                            u = sourceUVs[i].x,
                            v = sourceUVs[i].y
                        };
                    }
                }
                UnityEngine.Debug.Log($"Using source mesh UVs");
            }

            // 4. Handle Colors
            var colors = new VertexColor[numVertices];
            if (staticColors != null)
            {
                for (int i = 0; i < numVertices && i < staticColors.Length; i++)
                {
                    colors[i] = new VertexColor { 
                        r = staticColors[i].r,
                        g = staticColors[i].g,
                        b = staticColors[i].b,
                        a = staticColors[i].a
                    };
                }
                UnityEngine.Debug.Log($"Using provided static colors");
            }
            else
            {
                var sourceColors = sourceMesh.colors;
                for (int i = 0; i < numVertices; i++)
                {
                    if (sourceColors.Length > i)
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
                        colors[i] = new VertexColor { r = 1.0f, g = 1.0f, b = 1.0f, a = 1.0f };
                    }
                }
                UnityEngine.Debug.Log($"Using source mesh colors");
            }

            // 5. Get indices from source mesh
            var indices = new ushort[numIndices];
            // sourceIndices already declared earlier for validation
            for (int i = 0; i < numIndices; i++) indices[i] = (ushort)sourceIndices[i];

            // 6. Create the compressed animation object
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
                bit_widths_z = new byte[numVertices]
            };

            Array.Copy(quantizedFrames[0], anim.first_frame, numVertices);

            if (numFrames > 1)
            {
                // Track compression statistics
                int clampedDeltasX = 0, clampedDeltasY = 0, clampedDeltasZ = 0;
                int totalDeltasX = 0, totalDeltasY = 0, totalDeltasZ = 0;

                // First pass: calculate natural bit widths and count clamping needed
                for (int v = 0; v < numVertices; v++)
                {
                    int maxDx = 0, maxDy = 0, maxDz = 0;
                    for (int f = 1; f < numFrames; f++)
                    {
                        int dx = quantizedFrames[f][v].x - quantizedFrames[f - 1][v].x;
                        int dy = quantizedFrames[f][v].y - quantizedFrames[f - 1][v].y;
                        int dz = quantizedFrames[f][v].z - quantizedFrames[f - 1][v].z;
                        
                        if (Math.Abs(dx) > maxDx) maxDx = Math.Abs(dx);
                        if (Math.Abs(dy) > maxDy) maxDy = Math.Abs(dy);
                        if (Math.Abs(dz) > maxDz) maxDz = Math.Abs(dz);
                        
                        totalDeltasX++;
                        totalDeltasY++;
                        totalDeltasZ++;
                        
                        if (Math.Abs(dx) > maxDeltaX) clampedDeltasX++;
                        if (Math.Abs(dy) > maxDeltaY) clampedDeltasY++;
                        if (Math.Abs(dz) > maxDeltaZ) clampedDeltasZ++;
                    }
                    
                    // Use limited bit widths instead of natural ones
                    byte naturalBitsX = BitsForDelta(maxDx);
                    byte naturalBitsY = BitsForDelta(maxDy);
                    byte naturalBitsZ = BitsForDelta(maxDz);
                    
                    anim.bit_widths_x[v] = (byte)Math.Min(naturalBitsX, maxBitsX);
                    anim.bit_widths_y[v] = (byte)Math.Min(naturalBitsY, maxBitsY);
                    anim.bit_widths_z[v] = (byte)Math.Min(naturalBitsZ, maxBitsZ);
                }

                // Log compression statistics
                float clampPercentX = totalDeltasX > 0 ? (clampedDeltasX * 100.0f / totalDeltasX) : 0;
                float clampPercentY = totalDeltasY > 0 ? (clampedDeltasY * 100.0f / totalDeltasY) : 0;
                float clampPercentZ = totalDeltasZ > 0 ? (clampedDeltasZ * 100.0f / totalDeltasZ) : 0;
                
                UnityEngine.Debug.Log($"Compression stats: X={clampedDeltasX}/{totalDeltasX} ({clampPercentX:F1}% clamped), " +
                                    $"Y={clampedDeltasY}/{totalDeltasY} ({clampPercentY:F1}% clamped), " +
                                    $"Z={clampedDeltasZ}/{totalDeltasZ} ({clampPercentZ:F1}% clamped)");

                // Second pass: encode deltas with clamping and error carry-over
                var writer = new BitstreamWriter();
                
                // Track carry-over errors for each vertex
                var carryErrorX = new float[numVertices];
                var carryErrorY = new float[numVertices];
                var carryErrorZ = new float[numVertices];
                
                // Track actual positions (for error calculation)
                var actualPosX = new float[numVertices];
                var actualPosY = new float[numVertices];
                var actualPosZ = new float[numVertices];
                
                // Initialize with first frame positions
                for (uint v = 0; v < numVertices; v++)
                {
                    actualPosX[v] = quantizedFrames[0][v].x;
                    actualPosY[v] = quantizedFrames[0][v].y;
                    actualPosZ[v] = quantizedFrames[0][v].z;
                }
                
                for (uint f = 1; f < numFrames; f++)
                {
                    for (uint v = 0; v < numVertices; v++)
                    {
                        // Calculate intended deltas
                        float intendedDx = quantizedFrames[f][v].x - actualPosX[v];
                        float intendedDy = quantizedFrames[f][v].y - actualPosY[v];
                        float intendedDz = quantizedFrames[f][v].z - actualPosZ[v];
                        
                        // Add carry-over error from previous frames
                        intendedDx += carryErrorX[v];
                        intendedDy += carryErrorY[v];
                        intendedDz += carryErrorZ[v];
                        
                        // Clamp to representable delta range
                        int clampedDx = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(intendedDx), -maxDeltaX, maxDeltaX);
                        int clampedDy = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(intendedDy), -maxDeltaY, maxDeltaY);
                        int clampedDz = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(intendedDz), -maxDeltaZ, maxDeltaZ);
                        
                        // Calculate and store carry-over error for next frame
                        carryErrorX[v] = intendedDx - clampedDx;
                        carryErrorY[v] = intendedDy - clampedDy;
                        carryErrorZ[v] = intendedDz - clampedDz;
                        
                        // Update actual positions
                        actualPosX[v] += clampedDx;
                        actualPosY[v] += clampedDy;
                        actualPosZ[v] += clampedDz;
                        
                        // Write the clamped deltas
                        writer.Write((uint)clampedDx, anim.bit_widths_x[v]);
                        writer.Write((uint)clampedDy, anim.bit_widths_y[v]);
                        writer.Write((uint)clampedDz, anim.bit_widths_z[v]);
                    }
                }
                
                // Calculate final error statistics
                float totalErrorX = 0, totalErrorY = 0, totalErrorZ = 0;
                float maxErrorX = 0, maxErrorY = 0, maxErrorZ = 0;
                int lastFrame = (int)numFrames - 1;
                
                for (uint v = 0; v < numVertices; v++)
                {
                    float errorX = Math.Abs(actualPosX[v] - quantizedFrames[lastFrame][v].x);
                    float errorY = Math.Abs(actualPosY[v] - quantizedFrames[lastFrame][v].y);
                    float errorZ = Math.Abs(actualPosZ[v] - quantizedFrames[lastFrame][v].z);
                    
                    totalErrorX += errorX;
                    totalErrorY += errorY;
                    totalErrorZ += errorZ;
                    
                    if (errorX > maxErrorX) maxErrorX = errorX;
                    if (errorY > maxErrorY) maxErrorY = errorY;
                    if (errorZ > maxErrorZ) maxErrorZ = errorZ;
                }
                
                float avgErrorX = totalErrorX / numVertices;
                float avgErrorY = totalErrorY / numVertices;
                float avgErrorZ = totalErrorZ / numVertices;
                
                UnityEngine.Debug.Log($"Final position errors: X avg={avgErrorX:F2} max={maxErrorX:F2}, " +
                                    $"Y avg={avgErrorY:F2} max={maxErrorY:F2}, " +
                                    $"Z avg={avgErrorZ:F2} max={maxErrorZ:F2}");
                writer.Flush();
                anim.delta_stream = writer.ToArray();
            }
            else
            {
                anim.delta_stream = Array.Empty<uint>();
            }
            
            // Debug: Log final bounds that will be stored in the file
            UnityEngine.Debug.Log($"Final stored bounds: Min({anim.min_x:F3}, {anim.min_y:F3}, {anim.min_z:F3}) Max({anim.max_x:F3}, {anim.max_y:F3}, {anim.max_z:F3})");
            
            return anim;
        }
    }
}
