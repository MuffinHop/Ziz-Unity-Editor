#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public static class RatSplitTests
{
    [MenuItem("Ziz/Tests/Run RAT Split Test")]
    public static void RunTest()
    {
        Debug.Log("RAT Split Test - start");

        // Build a small sample animation with simple vertex motion
        List<Vector3[]> frames = new List<Vector3[]>();
        // frame 0
        frames.Add(new Vector3[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(0,1,0), new Vector3(1,1,0) });
        // frame 1: small motion
        frames.Add(new Vector3[] { new Vector3(0.1f,0,0), new Vector3(1.1f,0,0), new Vector3(0.1f,1,0), new Vector3(1.1f,1,0) });
        // frame 2: more motion
        frames.Add(new Vector3[] { new Vector3(0.2f,0,0), new Vector3(1.2f,0,0), new Vector3(0.2f,1,0), new Vector3(1.2f,1,0) });

        var mesh = new Mesh();
        mesh.vertices = frames[0];
        mesh.triangles = new int[] { 0, 1, 2, 2, 1, 3 };
        mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.colors = new Color[] { Color.white, Color.red, Color.green, Color.blue };

        var compressed = Rat.Tool.CompressFromFrames(frames, mesh, mesh.uv, mesh.colors, preserveFirstFrame: false, maxBitsX:8, maxBitsY:8, maxBitsZ:8);
        string baseName = Path.Combine(Application.dataPath.Replace("Assets", "GeneratedData"), "rat_split_test");
        if (!Directory.Exists(Path.GetDirectoryName(baseName))) Directory.CreateDirectory(Path.GetDirectoryName(baseName));

    // For testing only: enable heavier per-chunk validation to catch more issues while debugging
    Rat.Tool.enableHeavyValidation = true;
    var files = Rat.Tool.WriteRatFileWithSizeSplitting(baseName, compressed, maxFileSizeKB: 1, processedFrames: frames); // low limit to force splits
    Rat.Tool.enableHeavyValidation = false;
        Debug.Log($"RAT Split Test: created {files.Count} files: {string.Join(",", files)}");

        // Read back each RAT and attempt to decompress frames
        for (int i = 0; i < files.Count; i++)
        {
            var anim = Rat.Core.ReadRatFile(files[i]);
            var ctx = Rat.Core.CreateDecompressionContext(anim);
            var outFrame = new Vector3[anim.num_vertices];
            Rat.Core.DecompressToFrame(ctx, anim, 0);
            // Nothing to assert here; log for inspection
            Debug.Log($"RAT file {i}: frames={anim.num_frames}, verts={anim.num_vertices}");
        }

        // Additionally: combine the chunks and validate whole animation data against original frames
        if (files.Count > 1)
        {
            var composite = new Rat.CompressedAnimation
            {
                num_vertices = compressed.num_vertices,
                num_indices = compressed.num_indices,
                min_x = compressed.min_x, min_y = compressed.min_y, min_z = compressed.min_z,
                max_x = compressed.max_x, max_y = compressed.max_y, max_z = compressed.max_z,
                bit_widths_x = compressed.bit_widths_x,
                bit_widths_y = compressed.bit_widths_y,
                bit_widths_z = compressed.bit_widths_z,
                first_frame = null,
                isFirstFrameRaw = false
            };
            var deltaWords = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < files.Count; ++i)
            {
                var a = Rat.Core.ReadRatFile(files[i]);
                if (i == 0)
                {
                    composite.first_frame = a.first_frame;
                    composite.isFirstFrameRaw = a.isFirstFrameRaw;
                    if (a.first_frame_raw != null) composite.first_frame_raw = a.first_frame_raw;
                }
                if (a.delta_stream != null && a.delta_stream.Length > 0)
                {
                    deltaWords.AddRange(a.delta_stream);
                }
            }
            composite.delta_stream = deltaWords.ToArray();
            composite.num_frames = compressed.num_frames; // expected full anim frames
            var ctx2 = Rat.Core.CreateDecompressionContext(composite);
            // Decompress and compare a few frames
            uint framesToCheck = (uint)Mathf.Min((int)composite.num_frames, frames.Count);
            float maxErr = 0f;
            for (uint fIdx = 0; fIdx < framesToCheck; ++fIdx)
            {
                Rat.Core.DecompressToFrame(ctx2, composite, fIdx);
                for (int v = 0; v < composite.num_vertices; ++v)
                {
                    var q = ctx2.current_positions[v];
                    float x = composite.min_x + (q.x / 255f) * (composite.max_x - composite.min_x);
                    float y = composite.min_y + (q.y / 255f) * (composite.max_y - composite.min_y);
                    float z = composite.min_z + (q.z / 255f) * (composite.max_z - composite.min_z);
                    var exp = frames[(int)fIdx][v];
                    float err = Vector3.Distance(new Vector3(x, y, z), exp);
                    if (err > maxErr) maxErr = err;
                }
            }
            Debug.Log($"Composite RAT validation max error across {framesToCheck} frames: {maxErr:F6}");
        }

        Debug.Log("RAT Split Test - done");
    }
}
#endif
