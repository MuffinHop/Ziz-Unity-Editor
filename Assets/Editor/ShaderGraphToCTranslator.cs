using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[Serializable]
public class SerializedObject
{
    public string m_Type;
    public string m_ObjectId;
}

[Serializable]
public class GraphData
{
    public List<NodeRef> m_Nodes;
    public List<Edge> m_Edges;
    public OutputNode m_OutputNode;
}

[Serializable]
public class NodeRef
{
    public string m_Id;
}

[Serializable]
public class OutputNode
{
    public string m_Id;
}

[Serializable]
public class Node
{
    public string m_Name;
    public List<SlotRef> m_Slots;
    public int m_BlendMode;
}

[Serializable]
public class SlotRef
{
    public string m_Id;
}

[Serializable]
public class Slot
{
    public int m_Id;
    public string m_DisplayName;
    public int m_SlotType;
}

[Serializable]
public class Edge
{
    public SlotRef m_OutputSlot;
    public SlotRef m_InputSlot;
}

public class ShaderGraphToCTranslator : EditorWindow
{
    private string inputShaderGraphPath = "Assets/Shaders/MyShaderGraph.shadergraph";
    private string outputCPath = "Assets/MyShaderGraph.c";
    private static double estimatedTotalMs = 0.0;

    [MenuItem("Tools/Shader Graph to C Translator")]
    public static void ShowWindow()
    {
        GetWindow<ShaderGraphToCTranslator>("Shader Graph to C Translator");
    }

    void OnGUI()
    {
        GUILayout.Label("Shader Graph to C Translator", EditorStyles.boldLabel);
        GUILayout.Label("Translate Unity Shader Graph (.shadergraph) to C code using AllNodes.c.");

        EditorGUILayout.Space();

        inputShaderGraphPath = EditorGUILayout.TextField("Input Shader Graph Path", inputShaderGraphPath);
        outputCPath = EditorGUILayout.TextField("Output C Path", outputCPath);

        EditorGUILayout.Space();

        if (GUILayout.Button("Translate"))
        {
            TranslateShaderGraphToC(inputShaderGraphPath, outputCPath);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Translation Complete", "C code saved to " + outputCPath, "OK");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Estimated N64 Execution Time", (estimatedTotalMs * 320 * 240).ToString("F6") + " ms");
    }

    public static void TranslateShaderGraphToC(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            Debug.LogError("Input shader graph file not found: " + inputPath);
            return;
        }

        // Dictionary of function names to estimated ms on N64, these are a complete fabrication, but gives an idea
        Dictionary<string, double> functionTimes = new Dictionary<string, double>
        {
            // Artistic
            {"Unity_ChannelMixer_float", 0.00053},
            {"Unity_Contrast_float", 0.00043},
            {"Unity_Hue_Degrees_float", 0.0021},
            {"Unity_InvertColors_float4", 0.00013},
            {"Unity_ReplaceColor_float", 0.00053},
            {"Unity_Saturation_float", 0.00032},
            {"Unity_WhiteBalance_float", 0.00032},
            // Blend (approximate per mode)
            {"Unity_Blend_Screen_float4", 0.0005},
            {"Unity_Blend_Burn_float4", 0.0005},
            {"Unity_Blend_Darken_float4", 0.0005},
            {"Unity_Blend_Difference_float4", 0.0005},
            {"Unity_Blend_Dodge_float4", 0.0005},
            {"Unity_Blend_Divide_float4", 0.0005},
            {"Unity_Blend_Exclusion_float4", 0.0005},
            {"Unity_Blend_HardLight_float4", 0.0005},
            {"Unity_Blend_HardMix_float4", 0.0005},
            {"Unity_Blend_Lighten_float4", 0.0005},
            {"Unity_Blend_LinearBurn_float4", 0.0005},
            {"Unity_Blend_LinearDodge_float4", 0.0005},
            {"Unity_Blend_LinearLight_float4", 0.0005},
            {"Unity_Blend_Multiply_float4", 0.0005},
            {"Unity_Blend_Negation_float4", 0.0005},
            {"Unity_Blend_Overlay_float4", 0.0005},
            {"Unity_Blend_PinLight_float4", 0.0005},
            {"Unity_Blend_SoftLight_float4", 0.0005},
            {"Unity_Blend_Subtract_float4", 0.0005},
            {"Unity_Blend_VividLight_float4", 0.0005},
            {"Unity_Blend_Overwrite_float4", 0.0005},
            {"Unity_Dither_float4", 0.0005},
            {"Unity_ChannelMask_RedGreen_float4", 0.0005},
            {"Unity_ColorMask_float", 0.0005},
            {"Unity_NormalBlend_float", 0.00032},
            {"Unity_NormalFromHeight_Tangent_float", 0.0005},
            {"Unity_NormalStrength_float", 0.00032},
            {"Unity_NormalUnpack_float", 0.00032},
            {"Unity_Combine_float", 0.00011},
            {"Unity_Flip_float4", 0.00013},
            // Input
            {"Unity_Time_float", 0.00005},
            {"Unity_Vector1_float", 0.00003},
            {"Unity_Vector2_float", 0.00003},
            {"Unity_Vector3_float", 0.00003},
            {"Unity_Vector4_float", 0.00003},
            {"Unity_Matrix4x4_float", 0.00021},
            {"Unity_Texture2D_float", 0.00003},
            {"Unity_SamplerState_float", 0.00003},
            {"Unity_Constant_float", 0.00003},
            {"Unity_Property_float", 0.00003},
            // Math
            {"Unity_Add_float", 0.00001},
            {"Unity_Add_float2", 0.0001},
            {"Unity_Add_float3", 0.0001},
            {"Unity_Add_float4", 0.00013},
            {"Unity_Subtract_float", 0.00001},
            {"Unity_Subtract_float2", 0.0001},
            {"Unity_Subtract_float3", 0.0001},
            {"Unity_Subtract_float4", 0.00013},
            {"Unity_Multiply_float", 0.00005},
            {"Unity_Multiply_float2", 0.00011},
            {"Unity_Multiply_float3", 0.00016},
            {"Unity_Multiply_float4", 0.00021},
            {"Unity_Divide_float", 0.00013},
            {"Unity_Divide_float2", 0.00026},
            {"Unity_Divide_float3", 0.00039},
            {"Unity_Divide_float4", 0.00052},
            {"Unity_Power_float", 0.00021},
            {"Unity_SquareRoot_float", 0.00013},
            {"Unity_Log_float", 0.00016},
            {"Unity_Exp_float", 0.00016},
            {"Unity_Absolute_float", 0.00003},
            {"Unity_Negate_float", 0.00003},
            {"Unity_Sign_float", 0.00003},
            {"Unity_Floor_float", 0.00003},
            {"Unity_Ceil_float", 0.00003},
            {"Unity_Round_float", 0.00003},
            {"Unity_Truncate_float", 0.00003},
            {"Unity_Fraction_float", 0.00003},
            {"Unity_Modulo_float", 0.00013},
            {"Unity_Maximum_float", 0.00003},
            {"Unity_Minimum_float", 0.00003},
            {"Unity_Clamp_float", 0.00006},
            {"Unity_Saturate_float", 0.00003},
            {"Unity_Lerp_float", 0.00011},
            {"Unity_Lerp_float2", 0.00022},
            {"Unity_Lerp_float3", 0.00033},
            {"Unity_Lerp_float4", 0.00044},
            {"Unity_Smoothstep_float", 0.00011},
            {"Unity_OneMinus_float", 0.00003},
            {"Unity_Reciprocal_float", 0.00013},
            {"Unity_DegreesToRadians_float", 0.00003},
            {"Unity_RadiansToDegrees_float", 0.00003},
            {"Unity_Distance_float", 0.00027},
            {"Unity_Length_float", 0.00027},
            {"Unity_Normalize_float", 0.00032},
            {"Unity_CrossProduct_float", 0.00037},
            {"Unity_DotProduct_float", 0.00021},
            // Procedural/UV/Utility
            {"Unity_Checkerboard_float", 0.00053},
            {"Unity_GradientNoise_float", 0.0011},
            {"Unity_SimpleNoise_float", 0.0011},
            {"Unity_Voronoi_float", 0.0021},
            {"Unity_TilingAndOffset_float", 0.00011},
            {"Unity_Rotate_float", 0.00053},
            {"Unity_Spherize_float", 0.00053},
            {"Unity_Twirl_float", 0.00053},
            {"Unity_Branch_float", 0.00005},
            {"Unity_Branch_float2", 0.00011},
            {"Unity_Branch_float3", 0.00016},
            {"Unity_Branch_float4", 0.00021},
            {"Unity_Preview_float", 0.00003},
            {"Unity_Preview_float2", 0.00003},
            {"Unity_Preview_float3", 0.00003},
            {"Unity_Preview_float4", 0.00003},
            {"Unity_SceneColor_float", 0.00003},
            {"Unity_SceneDepth_Raw_float", 0.00003},
            // Default for others
            {"default", 0.01}
        };

        estimatedTotalMs = 0.0;

        string json = File.ReadAllText(inputPath);
        GraphData data = null;
        Dictionary<string, Node> nodes = new Dictionary<string, Node>();
        Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        Dictionary<string, string> slotToNode = new Dictionary<string, string>();

        string[] parts = json.Split(new string[] { "}\n\n{" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (i > 0 && !part.StartsWith("{")) part = "{" + part;
            if (i < parts.Length - 1 && !part.EndsWith("}")) part += "}";
            SerializedObject obj = JsonUtility.FromJson<SerializedObject>(part);
            if (obj == null) continue;
            if (obj.m_Type == "UnityEditor.ShaderGraph.GraphData")
            {
                data = JsonUtility.FromJson<GraphData>(part);
            }
            else if (obj.m_Type.EndsWith("Node"))
            {
                Node node = JsonUtility.FromJson<Node>(part);
                nodes[obj.m_ObjectId] = node;
            }
            else if (obj.m_Type.EndsWith("Slot"))
            {
                Slot slot = JsonUtility.FromJson<Slot>(part);
                slots[obj.m_ObjectId] = slot;
            }
        }

        if (data == null || data.m_Nodes == null)
        {
            Debug.LogError("Failed to parse shader graph JSON.");
            return;
        }

        // Build slotToNode
        foreach (var nodeRef in data.m_Nodes)
        {
            if (!nodes.ContainsKey(nodeRef.m_Id)) continue;
            Node node = nodes[nodeRef.m_Id];
            foreach (var slotRef in node.m_Slots)
            {
                slotToNode[slotRef.m_Id] = nodeRef.m_Id;
            }
        }

        StringBuilder cCode = new StringBuilder();
        cCode.AppendLine("#include \"AllNodes.c\"");
        cCode.AppendLine("#include <stdlib.h>");
        cCode.AppendLine("");
        cCode.AppendLine("// Generated C code from Shader Graph");
        cCode.AppendLine("void ShaderMain(float4* output /* add inputs as needed */) {");

        // Build dependency graph for topological sorting
        Dictionary<string, List<string>> dependencies = new Dictionary<string, List<string>>();
        Dictionary<string, int> inDegree = new Dictionary<string, int>();
        foreach (var nodeRef in data.m_Nodes)
        {
            dependencies[nodeRef.m_Id] = new List<string>();
            inDegree[nodeRef.m_Id] = 0;
        }
        foreach (var edge in data.m_Edges)
        {
            string inputNodeId = slotToNode.ContainsKey(edge.m_InputSlot.m_Id) ? slotToNode[edge.m_InputSlot.m_Id] : null;
            string outputNodeId = slotToNode.ContainsKey(edge.m_OutputSlot.m_Id) ? slotToNode[edge.m_OutputSlot.m_Id] : null;
            if (inputNodeId != null && outputNodeId != null && inputNodeId != outputNodeId)
            {
                dependencies[inputNodeId].Add(outputNodeId);
                inDegree[outputNodeId]++;
            }
        }

        // Topological sort using Kahn's algorithm
        Queue<string> queue = new Queue<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0) queue.Enqueue(kvp.Key);
        }
        List<string> sortedNodes = new List<string>();
        while (queue.Count > 0)
        {
            string nodeId = queue.Dequeue();
            sortedNodes.Add(nodeId);
            foreach (string dependent in dependencies[nodeId])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0) queue.Enqueue(dependent);
            }
        }
        if (sortedNodes.Count != data.m_Nodes.Count)
        {
            Debug.LogWarning("Cycle detected in shader graph; processing in original order.");
            sortedNodes = data.m_Nodes.Select(n => n.m_Id).ToList();
        }

        // Dictionary to track variable names for nodes
        Dictionary<string, string> nodeVars = new Dictionary<string, string>();
        // Dictionary to track output types for nodes
        Dictionary<string, string> nodeTypes = new Dictionary<string, string>();
        int varCounter = 0;

        // Initialize types based on node names
        foreach (var nodeRef in data.m_Nodes)
        {
            if (!nodes.ContainsKey(nodeRef.m_Id)) continue;
            Node node = nodes[nodeRef.m_Id];
            string type = GetNodeOutputType(node.m_Name);
            nodeTypes[nodeRef.m_Id] = type;
        }

        // Propagate types through edges
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in data.m_Edges)
            {
                string inputNodeId = slotToNode.ContainsKey(edge.m_InputSlot.m_Id) ? slotToNode[edge.m_InputSlot.m_Id] : null;
                string outputNodeId = slotToNode.ContainsKey(edge.m_OutputSlot.m_Id) ? slotToNode[edge.m_OutputSlot.m_Id] : null;
                if (inputNodeId != null && outputNodeId != null && nodeTypes.ContainsKey(outputNodeId))
                {
                    string outputType = nodeTypes[outputNodeId];
                    if (nodeTypes[inputNodeId] != outputType)
                    {
                        nodeTypes[inputNodeId] = outputType;
                        changed = true;
                    }
                }
            }
        }

        // Process nodes in topological order
        foreach (var nodeId in sortedNodes)
        {
            if (!nodes.ContainsKey(nodeId)) continue;
            Node node = nodes[nodeId];
            string varName = $"var{varCounter++}";
            nodeVars[nodeId] = varName;

            // Map node name to AllNodes function
            string type = nodeTypes.ContainsKey(nodeId) ? nodeTypes[nodeId] : "float4";
            string funcName = GetAllNodesFunctionName(node.m_Name, node.m_BlendMode, type);
            if (string.IsNullOrEmpty(funcName))
            {
                cCode.AppendLine($"    // Unsupported node: {node.m_Name}");
                continue;
            }

            // Generate function call
            List<string> args = new List<string>();
            foreach (var slotRef in node.m_Slots)
            {
                if (!slots.ContainsKey(slotRef.m_Id)) continue;
                Slot slot = slots[slotRef.m_Id];
                if (slot.m_SlotType == 0) // input
                {
                    var edge = data.m_Edges.FirstOrDefault(e => e.m_InputSlot.m_Id == slotRef.m_Id);
                    if (edge != null)
                    {
                        string outputNodeId = GetNodeIdFromSlot(edge.m_OutputSlot.m_Id, slotToNode);
                        args.Add(nodeVars[outputNodeId]);
                    }
                    else
                    {
                        args.Add("0.0f"); // Placeholder
                    }
                }
            }

            // Output slot
            var outputSlotRef = node.m_Slots.FirstOrDefault(s => slots.ContainsKey(s.m_Id) && slots[s.m_Id].m_SlotType == 1);
            if (outputSlotRef != null)
            {
                args.Add($"&{varName}");
            }

            cCode.AppendLine($"    {funcName}({string.Join(", ", args)});");

            // Accumulate estimated time
            estimatedTotalMs += functionTimes.ContainsKey(funcName) ? functionTimes[funcName] : functionTimes["default"];
        }

        // Set output
        if (data.m_OutputNode != null && !string.IsNullOrEmpty(data.m_OutputNode.m_Id) && nodeVars.ContainsKey(data.m_OutputNode.m_Id))
        {
            cCode.AppendLine($"    *output = {nodeVars[data.m_OutputNode.m_Id]};");
        }

        cCode.AppendLine("}");

        File.WriteAllText(outputPath, cCode.ToString());
        Debug.Log("Translation complete. Output saved to " + outputPath);
    }

    private static string GetAllNodesFunctionName(string nodeName, int blendMode = 0, string type = "float4")
    {
        // Basic mapping from Shader Graph node names to AllNodes functions
        // Append type suffix for math operations
        string suffix = "";
        if (type != "float4" && (nodeName == "Add" || nodeName == "Subtract" || nodeName == "Multiply" || nodeName == "Divide" || nodeName == "Power" || nodeName == "Maximum" || nodeName == "Minimum" || nodeName == "Lerp" || nodeName == "Clamp" || nodeName == "Saturate" || nodeName == "OneMinus" || nodeName == "Reciprocal" || nodeName == "Branch"))
        {
            suffix = "_" + type.Replace("float", "");
        }
        else if (type == "float")
        {
            suffix = "_float";
        }
        else if (type == "float2")
        {
            suffix = "_float2";
        }
        else if (type == "float3")
        {
            suffix = "_float3";
        }
        else
        {
            suffix = "_float4"; // default
        }

        switch (nodeName)
        {
            case "Add": return "Unity_Add" + suffix;
            case "Subtract": return "Unity_Subtract" + suffix;
            case "Multiply": return "Unity_Multiply" + suffix;
            case "Divide": return "Unity_Divide" + suffix;
            case "Power": return "Unity_Power" + suffix;
            case "SquareRoot": return "Unity_SquareRoot" + suffix;
            case "Log": return "Unity_Log" + suffix;
            case "Exp": return "Unity_Exp" + suffix;
            case "Absolute": return "Unity_Absolute" + suffix;
            case "Negate": return "Unity_Negate" + suffix;
            case "Sign": return "Unity_Sign" + suffix;
            case "Floor": return "Unity_Floor" + suffix;
            case "Ceil": return "Unity_Ceil" + suffix;
            case "Round": return "Unity_Round" + suffix;
            case "Truncate": return "Unity_Truncate" + suffix;
            case "Fraction": return "Unity_Fraction" + suffix;
            case "Modulo": return "Unity_Modulo" + suffix;
            case "Maximum": return "Unity_Maximum" + suffix;
            case "Minimum": return "Unity_Minimum" + suffix;
            case "Clamp": return "Unity_Clamp" + suffix;
            case "Saturate": return "Unity_Saturate" + suffix;
            case "Lerp": return "Unity_Lerp" + suffix;
            case "Smoothstep": return "Unity_Smoothstep" + suffix;
            case "OneMinus": return "Unity_OneMinus" + suffix;
            case "Reciprocal": return "Unity_Reciprocal" + suffix;
            case "DegreesToRadians": return "Unity_DegreesToRadians" + suffix;
            case "RadiansToDegrees": return "Unity_RadiansToDegrees" + suffix;
            case "Distance": return "Unity_Distance" + suffix;
            case "Length": return "Unity_Length" + suffix;
            case "Normalize": return "Unity_Normalize" + suffix;
            case "CrossProduct": return "Unity_CrossProduct" + suffix;
            case "DotProduct": return "Unity_DotProduct" + suffix;
            case "Color": return "Unity_Vector3_float"; // Placeholder
            case "Vector1": return "Unity_Vector1_float";
            case "Vector2": return "Unity_Vector2_float";
            case "Vector3": return "Unity_Vector3_float";
            case "Vector4": return "Unity_Vector4_float";
            case "Vector 4": return "Unity_Vector4_float";
            case "Time": return "Unity_Time_float";
            case "Checkerboard": return "Unity_Checkerboard_float";
            case "GradientNoise": return "Unity_GradientNoise_float";
            case "SimpleNoise": return "Unity_SimpleNoise_float";
            case "Voronoi": return "Unity_Voronoi_float";
            case "TilingAndOffset": return "Unity_TilingAndOffset_float";
            case "Rotate": return "Unity_Rotate_float";
            case "Spherize": return "Unity_Spherize_float";
            case "Twirl": return "Unity_Twirl_float";
            case "Branch": return "Unity_Branch" + suffix;
            case "Preview": return "Unity_Preview" + suffix;
            case "SceneColor": return "Unity_SceneColor_float";
            case "SceneDepth": return "Unity_SceneDepth_Raw_float";
            // Artistic
            case "ChannelMixer": return "Unity_ChannelMixer_float";
            case "Contrast": return "Unity_Contrast_float";
            case "Hue": return "Unity_Hue_Degrees_float";
            case "InvertColors": return "Unity_InvertColors_float4";
            case "ReplaceColor": return "Unity_ReplaceColor_float";
            case "Saturation": return "Unity_Saturation_float";
            case "WhiteBalance": return "Unity_WhiteBalance_float";
            // Blend
            case "Blend":
                string[] blendNames = { "Burn", "Darken", "Difference", "Dodge", "Divide", "Exclusion", "HardLight", "HardMix", "LinearBurn", "LinearDodge", "LinearLight", "Multiply", "Negation", "Overlay", "PinLight", "Screen", "SoftLight", "Subtract", "VividLight", "Overwrite" };
                if (blendMode >= 0 && blendMode < blendNames.Length)
                    return "Unity_Blend_" + blendNames[blendMode] + "_float4";
                else
                    return "Unity_Blend_Add_float4"; // default
            case "Dither": return "Unity_Dither_float4";
            case "ChannelMask": return "Unity_ChannelMask_RedGreen_float4";
            case "ColorMask": return "Unity_ColorMask_float";
            case "NormalBlend": return "Unity_NormalBlend_float";
            case "NormalFromHeight": return "Unity_NormalFromHeight_Tangent_float";
            case "NormalStrength": return "Unity_NormalStrength_float";
            case "NormalUnpack": return "Unity_NormalUnpack_float";
            case "Combine": return "Unity_Combine_float";
            case "Flip": return "Unity_Flip_float4";
            case "Matrix2x2": return "Unity_Matrix2x2_float";
            case "Matrix3x3": return "Unity_Matrix3x3_float";
            case "Matrix4x4": return "Unity_Matrix4x4_float";
            case "Texture2D": return "Unity_Texture2D_float";
            case "SamplerState": return "Unity_Sam`plerState_float";
            case "Constant": return "Unity_Constant_float";
            case "Property": return "Unity_Property_float";
            case "Blackbody": return "Unity_Blackbody_float";
            case "Gradient": return "Unity_Gradient_float";
            case "SampleGradient": return "Unity_SampleGradient_float";
            case "Triplanar": return "Unity_Triplanar_float";
            case "PolarCoordinates": return "Unity_PolarCoordinates_float";
            case "RadialShear": return "Unity_RadialShear_float";
            case "RadialZoom": return "Unity_RadialZoom_float"; 
            case "ScreenParams": return "Unity_ScreenParams_float";
            case "ZBufferParams": return "Unity_ZBufferParams_float";
            case "ProjectionParams": return "Unity_ProjectionParams_float";
            case "CameraProjection": return "Unity_CameraProjection_float";
            case "CameraInvProjection": return "Unity_CameraInvProjection_float";
            case "CameraView": return "Unity_CameraView_float";
            case "CameraInvView": return "Unity_CameraInvView_float";
            case "CameraViewProjection": return "Unity_CameraViewProjection_float";
            case "CameraInvViewProjection": return "Unity_CameraInvViewProjection_float";
            case "ObjectToWorld": return "Unity_ObjectToWorld_float";
            case "WorldToObject": return "Unity_WorldToObject_float";
            case "AbsoluteWorldSpacePosition": return "Unity_AbsoluteWorldSpacePosition_float";
            case "RelativeWorldSpacePosition": return "Unity_RelativeWorldSpacePosition_float";
            case "AbsoluteWorldSpaceViewDirection": return "Unity_AbsoluteWorldSpaceViewDirection_float";
            case "RelativeWorldSpaceViewDirection": return "Unity_RelativeWorldSpaceViewDirection_float";
            case "WorldSpaceNormal": return "Unity_WorldSpaceNormal_float";
            case "ObjectSpacePosition": return "Unity_ObjectSpacePosition_float";
            case "ObjectSpaceNormal": return "Unity_ObjectSpaceNormal_float";
            case "ObjectSpaceTangent": return "Unity_ObjectSpaceTangent_float";
            case "ObjectSpaceBitangent": return "Unity_ObjectSpaceBitangent_float";
            case "ObjectSpaceViewDirection": return "Unity_ObjectSpaceViewDirection_float";
            case "TangentSpaceNormal": return "Unity_TangentSpaceNormal_float";
            case "TangentSpaceTangent": return "Unity_TangentSpaceTangent_float";
            case "TangentSpaceBitangent": return "Unity_TangentSpaceBitangent_float";
            case "TangentSpaceViewDirection": return "Unity_TangentSpaceViewDirection_float";
            case "All": return "Unity_All_float";
            case "Any": return "Unity_Any_float";
            case "IsNaN": return "Unity_IsNaN_float";
            case "IsInfinite": return "Unity_IsInfinite_float";
            case "Comparison": return "Unity_Comparison_float";
            case "Arctangent2": return "Unity_Arctangent2_float";
            case "Cosine": return "Unity_Cosine_float";
            case "Sine": return "Unity_Sine_float";
            case "Tangent": return "Unity_Tangent_float";
            case "HyperbolicCosine": return "Unity_HyperbolicCosine_float";
            case "HyperbolicSine": return "Unity_HyperbolicSine_float";
            case "HyperbolicTangent": return "Unity_HyperbolicTangent_float";
            case "Noise": return "Unity_Noise_float";
            // Add more mappings as needed
            default: return null;
        }
    }

    private static string GetNodeIdFromSlot(string slotId, Dictionary<string, string> slotToNode)
    {
        return slotToNode.ContainsKey(slotId) ? slotToNode[slotId] : null;
    }

    private static string GetNodeOutputType(string nodeName)
    {
        // Basic type mapping based on node name
        switch (nodeName)
        {
            case "Vector1": return "float";
            case "Vector2": return "float2";
            case "Vector3": return "float3";
            case "Vector4": return "float4";
            case "Vector 4": return "float4";
            case "Color": return "float3"; // Assuming RGB
            case "Time": return "float4"; // Time outputs multiple
            case "Checkerboard": return "float3";
            case "GradientNoise": return "float";
            case "SimpleNoise": return "float";
            case "Voronoi": return "float";
            case "TilingAndOffset": return "float2";
            case "Rotate": return "float2";
            case "Spherize": return "float2";
            case "Twirl": return "float2";
            case "Branch": return "float"; // Assuming scalar predicate
            case "Preview": return "float"; // Placeholder
            case "SceneColor": return "float3";
            case "SceneDepth": return "float";
            // Artistic
            case "ChannelMixer": return "float3";
            case "Contrast": return "float3";
            case "Hue": return "float3";
            case "InvertColors": return "float4";
            case "ReplaceColor": return "float3";
            case "Saturation": return "float3";
            case "WhiteBalance": return "float3";
            // Blend
            case "Blend": return "float4";
            case "Dither": return "float4";
            case "ChannelMask": return "float4";
            case "ColorMask": return "float";
            case "NormalBlend": return "float3";
            case "NormalFromHeight": return "float3";
            case "NormalStrength": return "float3";
            case "NormalUnpack": return "float3";
            case "Combine": return "float4"; // Assuming RGBA
            case "Flip": return "float4";
            case "Matrix2x2": return "float2x2";
            case "Matrix3x3": return "float3x3";
            case "Matrix4x4": return "float4x4";
            case "Texture2D": return "float4"; // Assuming RGBA
            case "SamplerState": return "void*"; // Placeholder
            case "Constant": return "float";
            case "Property": return "float";
            case "Blackbody": return "float3";
            case "Gradient": return "float4"; // Gradient struct
            case "SampleGradient": return "float4";
            case "Triplanar": return "float4";
            case "PolarCoordinates": return "float2";
            case "RadialShear": return "float2";
            case "RadialZoom": return "float2";
            case "CustomFunction": return "float";
            case "SubGraph": return "float";
            case "ScreenParams": return "float4";
            case "ZBufferParams": return "float4";
            case "ProjectionParams": return "float4";
            case "CameraProjection": return "float4x4";
            case "CameraInvProjection": return "float4x4";
            case "CameraView": return "float4x4";
            case "CameraInvView": return "float4x4";
            case "CameraViewProjection": return "float4x4";
            case "CameraInvViewProjection": return "float4x4";
            case "ObjectToWorld": return "float4x4";
            case "WorldToObject": return "float4x4";
            case "AbsoluteWorldSpacePosition": return "float3";
            case "RelativeWorldSpacePosition": return "float3";
            case "AbsoluteWorldSpaceViewDirection": return "float3";
            case "RelativeWorldSpaceViewDirection": return "float3";
            case "WorldSpaceNormal": return "float3";
            case "ObjectSpacePosition": return "float3";
            case "ObjectSpaceNormal": return "float3";
            case "ObjectSpaceTangent": return "float3";
            case "ObjectSpaceBitangent": return "float3";
            case "ObjectSpaceViewDirection": return "float3";
            case "TangentSpaceNormal": return "float3";
            case "TangentSpaceTangent": return "float3";
            case "TangentSpaceBitangent": return "float3";
            case "TangentSpaceViewDirection": return "float3";
            case "All": return "float";
            case "Any": return "float";
            case "IsNaN": return "float";
            case "IsInfinite": return "float";
            case "Comparison": return "float";
            case "Arctangent2": return "float";
            case "Cosine": return "float";
            case "Sine": return "float";
            case "Tangent": return "float";
            case "HyperbolicCosine": return "float";
            case "HyperbolicSine": return "float";
            case "HyperbolicTangent": return "float";
            case "Noise": return "float";
            // Math nodes default to float4 if not specified
            default: return "float4";
        }
    }
}