using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZizSceneEditor
{
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
        public NodeRef m_Node;     // added: support edges that reference node + slotId
        public int m_SlotId;       // added
    }

    [Serializable]
    public class Slot
    {
        public int m_Id;
        public string m_DisplayName;
        public int m_SlotType;
        public SlotValue m_Value;
        public SlotValue m_DefaultValue;
    }

    [Serializable]
    public class SlotValue
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    public class Edge
    {
        public SlotRef m_OutputSlot;
        public SlotRef m_InputSlot;
    }

    public class ShaderGraphToCTranslator : EditorWindow
    {
        // Add execution cost enum at class scope (moved out of method)
        private enum ExecutionCost { Light, Medium, Heavy, VeryHeavy }

        // Helper to infer node output type from node name
        private static string GetNodeOutputType(string nodeName)
        {
            // Simple mapping, expand as needed
            if (nodeName.Contains("float4")) return "float4";
            if (nodeName.Contains("float3")) return "float3";
            if (nodeName.Contains("float2")) return "float2";
            if (nodeName.Contains("float")) return "float";
            return "float4"; // Default
        }

        // Helper to get node ID from a SlotRef (handles both m_Id and m_Node+m_SlotId forms)
        private static string GetNodeIdFromSlot(SlotRef slotRef, Dictionary<string, string> slotToNode)
        {
            if (slotRef == null) return null;
            // prefer explicit slot object id if present
            if (!string.IsNullOrEmpty(slotRef.m_Id) && slotToNode.ContainsKey(slotRef.m_Id))
                return slotToNode[slotRef.m_Id];
            // fallback to node+slotId key if provided in the slotToNode map
            if (slotRef.m_Node != null)
            {
                string key = $"{slotRef.m_Node.m_Id}_{slotRef.m_SlotId}";
                if (slotToNode.ContainsKey(key)) return slotToNode[key];
                // if slotToNode doesn't contain the key, we still have the node id available on the slotRef
                return slotRef.m_Node.m_Id;
            }
            return null;
        }

        // Helper to map node name and blend mode/type to AllNodes function name
        private static string GetAllNodesFunctionName(string nodeName, int blendMode, string type)
        {
            // Example mapping logic, expand as needed
            if (nodeName.Contains("Scene Color")) return "Unity_SceneColor";
            if (nodeName.StartsWith("Blend"))
            {
                // Map blend mode to function name
                switch (blendMode)
                {
                    case 0: return "Unity_Blend_Overwrite_float4";
                    case 1: return "Unity_Blend_Multiply_float4";
                    case 2: return "Unity_Blend_Screen_float4";
                    // Add more blend modes as needed
                    default: return "Unity_Blend_Overwrite_float4";
                }
            }
            // Fallback: concatenate node name and type
            return $"Unity_{nodeName.Replace(" ", "")}_{type}";
        }

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

            // Replace flat double map with enums + cost table
            Dictionary<string, ExecutionCost> functionCosts = new Dictionary<string, ExecutionCost>
            {
                // Artistic (mapped to Medium)
                {"Unity_ChannelMixer_float", ExecutionCost.Heavy},
                {"Unity_Contrast_float", ExecutionCost.Medium},
                {"Unity_Hue_Degrees_float", ExecutionCost.Heavy},
                {"Unity_InvertColors_float4", ExecutionCost.Light},
                {"Unity_ReplaceColor_float", ExecutionCost.Medium},
                {"Unity_Saturation_float", ExecutionCost.Medium},
                {"Unity_WhiteBalance_float", ExecutionCost.Medium},
                // Blend (most mapped to Medium)
                {"Unity_Blend_Screen_float4", ExecutionCost.Medium},
                {"Unity_Blend_Burn_float4", ExecutionCost.Medium},
                {"Unity_Blend_Darken_float4", ExecutionCost.Medium},
                {"Unity_Blend_Difference_float4", ExecutionCost.Medium},
                {"Unity_Blend_Dodge_float4", ExecutionCost.Medium},
                {"Unity_Blend_Divide_float4", ExecutionCost.Medium},
                {"Unity_Blend_Exclusion_float4", ExecutionCost.Medium},
                {"Unity_Blend_HardLight_float4", ExecutionCost.Medium},
                {"Unity_Blend_HardMix_float4", ExecutionCost.Medium},
                {"Unity_Blend_Lighten_float4", ExecutionCost.Medium},
                {"Unity_Blend_LinearBurn_float4", ExecutionCost.Medium},
                {"Unity_Blend_LinearDodge_float4", ExecutionCost.Medium},
                {"Unity_Blend_LinearLight_float4", ExecutionCost.Medium},
                {"Unity_Blend_Multiply_float4", ExecutionCost.Medium},
                {"Unity_Blend_Negation_float4", ExecutionCost.Medium},
                {"Unity_Blend_Overlay_float4", ExecutionCost.Medium},
                {"Unity_Blend_PinLight_float4", ExecutionCost.Medium},
                {"Unity_Blend_SoftLight_float4", ExecutionCost.Medium},
                {"Unity_Blend_Subtract_float4", ExecutionCost.Medium},
                {"Unity_Blend_VividLight_float4", ExecutionCost.Medium},
                {"Unity_Blend_Overwrite_float4", ExecutionCost.Medium},
                {"Unity_Dither_float4", ExecutionCost.Medium},
                {"Unity_ChannelMask_RedGreen_float4", ExecutionCost.Medium},
                {"Unity_ColorMask_float", ExecutionCost.Medium},
                {"Unity_NormalBlend_float", ExecutionCost.Medium},
                {"Unity_NormalFromHeight_Tangent_float", ExecutionCost.Medium},
                {"Unity_NormalStrength_float", ExecutionCost.Medium},
                {"Unity_NormalUnpack_float", ExecutionCost.Medium},
                {"Unity_Combine_float", ExecutionCost.Light},
                {"Unity_Flip_float4", ExecutionCost.Light},
                // Input (Light)
                {"Unity_Time_float", ExecutionCost.Light},
                {"Unity_Vector1_float", ExecutionCost.Light},
                {"Unity_Vector2_float", ExecutionCost.Light},
                {"Unity_Vector3_float", ExecutionCost.Light},
                {"Unity_Vector4_float", ExecutionCost.Light},
                {"Unity_Matrix4x4_float", ExecutionCost.Medium},
                {"Unity_Texture2D_float", ExecutionCost.Light},
                {"Unity_SamplerState_float", ExecutionCost.Light},
                {"Unity_Constant_float", ExecutionCost.Light},
                {"Unity_Property_float", ExecutionCost.Light},
                // Math (mix of Light/Medium)
                {"Unity_Add_float", ExecutionCost.Light},
                {"Unity_Add_float2", ExecutionCost.Light},
                {"Unity_Add_float3", ExecutionCost.Light},
                {"Unity_Add_float4", ExecutionCost.Light},
                {"Unity_Subtract_float", ExecutionCost.Light},
                {"Unity_Subtract_float2", ExecutionCost.Light},
                {"Unity_Subtract_float3", ExecutionCost.Light},
                {"Unity_Subtract_float4", ExecutionCost.Light},
                {"Unity_Multiply_float", ExecutionCost.Medium},
                {"Unity_Multiply_float2", ExecutionCost.Medium},
                {"Unity_Multiply_float3", ExecutionCost.Medium},
                {"Unity_Multiply_float4", ExecutionCost.Medium},
                {"Unity_Divide_float", ExecutionCost.Medium},
                {"Unity_Divide_float2", ExecutionCost.Medium},
                {"Unity_Divide_float3", ExecutionCost.Medium},
                {"Unity_Divide_float4", ExecutionCost.Medium},
                {"Unity_Power_float", ExecutionCost.Medium},
                {"Unity_SquareRoot_float", ExecutionCost.Medium},
                {"Unity_Log_float", ExecutionCost.Medium},
                {"Unity_Exp_float", ExecutionCost.Medium},
                {"Unity_Absolute_float", ExecutionCost.Light},
                {"Unity_Negate_float", ExecutionCost.Light},
                {"Unity_Sign_float", ExecutionCost.Light},
                {"Unity_Floor_float", ExecutionCost.Light},
                {"Unity_Ceil_float", ExecutionCost.Light},
                {"Unity_Round_float", ExecutionCost.Light},
                {"Unity_Truncate_float", ExecutionCost.Light},
                {"Unity_Fraction_float", ExecutionCost.Light},
                {"Unity_Modulo_float", ExecutionCost.Medium},
                {"Unity_Maximum_float", ExecutionCost.Light},
                {"Unity_Minimum_float", ExecutionCost.Light},
                {"Unity_Clamp_float", ExecutionCost.Medium},
                {"Unity_Saturate_float", ExecutionCost.Light},
                {"Unity_Lerp_float", ExecutionCost.Medium},
                {"Unity_Lerp_float2", ExecutionCost.Medium},
                {"Unity_Lerp_float3", ExecutionCost.Medium},
                {"Unity_Lerp_float4", ExecutionCost.Medium},
                {"Unity_Smoothstep_float", ExecutionCost.Medium},
                {"Unity_OneMinus_float", ExecutionCost.Light},
                {"Unity_Reciprocal_float", ExecutionCost.Medium},
                {"Unity_DegreesToRadians_float", ExecutionCost.Light},
                {"Unity_RadiansToDegrees_float", ExecutionCost.Light},
                {"Unity_Distance_float", ExecutionCost.Medium},
                {"Unity_Length_float", ExecutionCost.Medium},
                {"Unity_Normalize_float", ExecutionCost.Medium},
                {"Unity_CrossProduct_float", ExecutionCost.Medium},
                {"Unity_DotProduct_float", ExecutionCost.Medium},
                // Procedural/UV/Utility (some heavy)
                {"Unity_Checkerboard_float", ExecutionCost.Medium},
                {"Unity_GradientNoise_float", ExecutionCost.Heavy},
                {"Unity_SimpleNoise_float", ExecutionCost.Heavy},
                {"Unity_Voronoi_float", ExecutionCost.Heavy},
                {"Unity_TilingAndOffset_float", ExecutionCost.Light},
                {"Unity_Rotate_float", ExecutionCost.Medium},
                {"Unity_Spherize_float", ExecutionCost.Medium},
                {"Unity_Twirl_float", ExecutionCost.Medium},
                {"Unity_Branch_float", ExecutionCost.Light},
                {"Unity_Branch_float2", ExecutionCost.Light},
                {"Unity_Branch_float3", ExecutionCost.Light},
                {"Unity_Branch_float4", ExecutionCost.Light},
                {"Unity_Preview_float", ExecutionCost.Light},
                {"Unity_Preview_float2", ExecutionCost.Light},
                {"Unity_Preview_float3", ExecutionCost.Light},
                {"Unity_Preview_float4", ExecutionCost.Light},
                {"Unity_SceneColor_float", ExecutionCost.Light},
                {"Unity_SceneDepth_Raw_float", ExecutionCost.Light},
                // default mapping omitted here; unknowns will fall back to VeryHeavy below
            };

            // numeric values for each enum category
            Dictionary<ExecutionCost, double> costValues = new Dictionary<ExecutionCost, double>
            {
                { ExecutionCost.Light, 0.0000107 },
                { ExecutionCost.Medium, 0.00006 },
                { ExecutionCost.Heavy, 0.00020 },
                { ExecutionCost.VeryHeavy, 0.0005 }
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
                    // map slot object id -> node id (most common)
                    if (!string.IsNullOrEmpty(slotRef.m_Id))
                    {
                        slotToNode[slotRef.m_Id] = nodeRef.m_Id;
                        // if we have a parsed Slot object for this slotRef, also map numeric slot id key:
                        if (slots.ContainsKey(slotRef.m_Id))
                        {
                            var parsedSlot = slots[slotRef.m_Id];
                            // numeric slot id exists in parsed slot object
                            string nodeSlotKey = $"{nodeRef.m_Id}_{parsedSlot.m_Id}";
                            if (!slotToNode.ContainsKey(nodeSlotKey))
                                slotToNode[nodeSlotKey] = nodeRef.m_Id;
                        }
                    }
                }
            }

            // Also add keys for edges that reference node+slotId (ensure slotToNode contains those keys)
            foreach (var edge in data.m_Edges)
            {
                if (edge?.m_OutputSlot != null && edge.m_OutputSlot.m_Node != null)
                {
                    string keyOut = $"{edge.m_OutputSlot.m_Node.m_Id}_{edge.m_OutputSlot.m_SlotId}";
                    if (!slotToNode.ContainsKey(keyOut))
                        slotToNode[keyOut] = edge.m_OutputSlot.m_Node.m_Id;
                }
                if (edge?.m_InputSlot != null && edge.m_InputSlot.m_Node != null)
                {
                    string keyIn = $"{edge.m_InputSlot.m_Node.m_Id}_{edge.m_InputSlot.m_SlotId}";
                    if (!slotToNode.ContainsKey(keyIn))
                        slotToNode[keyIn] = edge.m_InputSlot.m_Node.m_Id;
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
                string inputNodeId = (edge.m_InputSlot != null && edge.m_InputSlot.m_Id != null && slotToNode.ContainsKey(edge.m_InputSlot.m_Id)) ? slotToNode[edge.m_InputSlot.m_Id] : null;
                string outputNodeId = (edge.m_OutputSlot != null && edge.m_OutputSlot.m_Id != null && slotToNode.ContainsKey(edge.m_OutputSlot.m_Id)) ? slotToNode[edge.m_OutputSlot.m_Id] : null;
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

            // If no output node is set, default to the node feeding into the SurfaceDescription.BaseColor block
            if (string.IsNullOrEmpty(data.m_OutputNode?.m_Id))
            {
                var baseColorNode = data.m_Nodes.FirstOrDefault(n => nodes.ContainsKey(n.m_Id) && nodes[n.m_Id].m_Name == "SurfaceDescription.BaseColor");
                if (baseColorNode != null)
                {
                    // Find the edge that feeds into BaseColor input and pick its output node as the final output
                    var feedEdge = data.m_Edges.FirstOrDefault(e => e.m_InputSlot != null && e.m_InputSlot.m_Node != null && e.m_InputSlot.m_Node.m_Id == baseColorNode.m_Id);
                    if (feedEdge != null)
                    {
                        data.m_OutputNode = new OutputNode { m_Id = GetNodeIdFromSlot(feedEdge.m_OutputSlot, slotToNode) ?? feedEdge.m_OutputSlot.m_Node?.m_Id };
                    }
                    else
                    {
                        data.m_OutputNode = new OutputNode { m_Id = baseColorNode.m_Id };
                    }
                }
            }

            // Build reverse dependencies (from output node -> input nodes) to find nodes that affect the output
            Dictionary<string, List<string>> reverseDeps = new Dictionary<string, List<string>>();
            foreach (var edge in data.m_Edges)
            {
                string inputNodeId = GetNodeIdFromSlot(edge.m_InputSlot, slotToNode);
                string outputNodeId = GetNodeIdFromSlot(edge.m_OutputSlot, slotToNode);
                if (inputNodeId != null && outputNodeId != null)
                {
                    if (!reverseDeps.ContainsKey(outputNodeId)) reverseDeps[outputNodeId] = new List<string>();
                    reverseDeps[outputNodeId].Add(inputNodeId);
                }
            }

            // Collect relevant nodes (those that can reach the output node)
            HashSet<string> relevantNodes = new HashSet<string>();
            if (data.m_OutputNode != null && !string.IsNullOrEmpty(data.m_OutputNode.m_Id))
            {
                Queue<string> q = new Queue<string>();
                q.Enqueue(data.m_OutputNode.m_Id);
                relevantNodes.Add(data.m_OutputNode.m_Id);
                while (q.Count > 0)
                {
                    string nid = q.Dequeue();
                    if (!reverseDeps.ContainsKey(nid)) continue;
                    foreach (var dep in reverseDeps[nid])
                    {
                        if (!relevantNodes.Contains(dep))
                        {
                            relevantNodes.Add(dep);
                            q.Enqueue(dep);
                        }
                    }
                }
            }

            // Propagate types through edges, only for relevant nodes
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in data.m_Edges)
                {
                    string inputNodeId = GetNodeIdFromSlot(edge.m_InputSlot, slotToNode);
                    string outputNodeId = GetNodeIdFromSlot(edge.m_OutputSlot, slotToNode);
                    if (inputNodeId != null && outputNodeId != null && relevantNodes.Contains(inputNodeId) && relevantNodes.Contains(outputNodeId) && nodeTypes.ContainsKey(outputNodeId))
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

            // Declare constant variables for default values, only for relevant nodes
            Dictionary<string, string> constVars = new Dictionary<string, string>();
            int constCounter = 0;
            foreach (var nodeRef in data.m_Nodes)
            {
                if (!relevantNodes.Contains(nodeRef.m_Id) || !nodes.ContainsKey(nodeRef.m_Id)) continue;
                Node node = nodes[nodeRef.m_Id];
                string nodeType = nodeTypes.ContainsKey(nodeRef.m_Id) ? nodeTypes[nodeRef.m_Id] : "float4";
                foreach (var slotRef in node.m_Slots)
                {
                    if (!slots.ContainsKey(slotRef.m_Id)) continue;
                    Slot slot = slots[slotRef.m_Id];
                    // only input slots with explicit default/value
                    if (slot.m_SlotType == 0 && slot.m_Value != null &&
                        (Math.Abs(slot.m_Value.x) > 1e-9f || Math.Abs(slot.m_Value.y) > 1e-9f || Math.Abs(slot.m_Value.z) > 1e-9f || Math.Abs(slot.m_Value.w) > 1e-9f))
                    {
                        // determine if this specific slot is the target of any edge
                        bool slotConnected = data.m_Edges.Any(e =>
                        {
                            if (e?.m_InputSlot == null) return false;
                            // match by object id
                            if (!string.IsNullOrEmpty(e.m_InputSlot.m_Id) && e.m_InputSlot.m_Id == slotRef.m_Id) return true;
                            // or match by node + numeric slot id (parsed Slot.m_Id)
                            if (e.m_InputSlot.m_Node != null && slots.ContainsKey(slotRef.m_Id))
                            {
                                return e.m_InputSlot.m_Node.m_Id == nodeRef.m_Id && e.m_InputSlot.m_SlotId == slots[slotRef.m_Id].m_Id;
                            }
                            return false;
                        });

                        if (!slotConnected)
                        {
                            string constName = $"constVar{constCounter++}";
                            string valueStr;
                            if (nodeType.Contains("float4")) valueStr = $"{{{slot.m_Value.x:0.0}f, {slot.m_Value.y:0.0}f, {slot.m_Value.z:0.0}f, {slot.m_Value.w:0.0}f}}";
                            else if (nodeType.Contains("float3")) valueStr = $"{{{slot.m_Value.x:0.0}f, {slot.m_Value.y:0.0}f, {slot.m_Value.z:0.0}f}}";
                            else if (nodeType.Contains("float2")) valueStr = $"{{{slot.m_Value.x:0.0}f, {slot.m_Value.y:0.0}f}}";
                            else valueStr = $"{slot.m_Value.x:0.0}f";
                            cCode.AppendLine($"    {nodeType} {constName} = {valueStr};");
                            constVars[slotRef.m_Id] = constName;
                        }
                    }
                }
            }

            // Process nodes in topological order, only for relevant nodes
            foreach (var nodeId in sortedNodes)
            {
                if (!nodes.ContainsKey(nodeId)) continue;
                Node node = nodes[nodeId];

                // Map node name to AllNodes function first
                string type = nodeTypes.ContainsKey(nodeId) ? nodeTypes[nodeId] : "float4";
                string funcName = GetAllNodesFunctionName(node.m_Name, node.m_BlendMode, type);
                if (string.IsNullOrEmpty(funcName))
                {
                    cCode.AppendLine($"    // Unsupported node: {node.m_Name}");
                    continue;
                }

                // Generate function input tokens (names: varX or constVarY or "NULL")
                List<string> args = new List<string>();
                foreach (var slotRef in node.m_Slots)
                {
                    if (!slots.ContainsKey(slotRef.m_Id)) continue;
                    Slot slot = slots[slotRef.m_Id];
                    if (slot.m_SlotType == 0) // input
                    {
                        string arg = null;
                        // find edge that connects into this slot (match by object id or node+numeric slot id)
                        var edge = data.m_Edges.FirstOrDefault(e =>
                        {
                            if (e?.m_InputSlot == null) return false;
                            if (!string.IsNullOrEmpty(e.m_InputSlot.m_Id) && e.m_InputSlot.m_Id == slotRef.m_Id) return true;
                            if (e.m_InputSlot.m_Node != null && slots.ContainsKey(slotRef.m_Id))
                            {
                                return e.m_InputSlot.m_Node.m_Id == nodeId && e.m_InputSlot.m_SlotId == slots[slotRef.m_Id].m_Id;
                            }
                            return false;
                        });

                        if (edge != null)
                        {
                            string outputNodeId = GetNodeIdFromSlot(edge.m_OutputSlot, slotToNode);
                            if (outputNodeId != null && nodeVars.ContainsKey(outputNodeId))
                            {
                                arg = nodeVars[outputNodeId];
                            }
                            else
                            {
                                arg = "NULL"; // placeholder
                            }
                        }
                        else
                        {
                            // use constant if present for this slot object id
                            if (constVars.ContainsKey(slotRef.m_Id))
                            {
                                arg = constVars[slotRef.m_Id];
                            }
                            else
                            {
                                arg = "NULL";
                            }
                        }

                        if (arg != null) args.Add(arg);
                    }
                }

                // If this is a SurfaceDescription block, ignore emitting its function call:
                // forward first non-NULL input (or NULL) to represent its output.
                if (funcName.Contains("Unity_SurfaceDescription"))
                {
                    string forward = args.FirstOrDefault(a => a != "NULL") ?? "NULL";
                    // If forward is "NULL", keep it as-is; otherwise nodeVars should point to the named var/const.
                    nodeVars[nodeId] = forward;
                    // do not emit any var declaration or function call for SurfaceDescription nodes
                    continue;
                }

                // Otherwise, normal behavior: create a new temp var and call the function
                string varName = $"var{varCounter++}";
                nodeVars[nodeId] = varName;

                // Declare output variable
                cCode.AppendLine($"    {type} {varName};");
                // Call function with pointers for inputs and output
                string argsStr = string.Join(", ", args.Select(a => a == "NULL" ? "NULL" : $"&{a}"));
                cCode.AppendLine($"    {funcName}({argsStr}, &{varName});");
            }

            // Assign final output
            if (data.m_OutputNode != null && nodeVars.ContainsKey(data.m_OutputNode.m_Id))
            {
                cCode.AppendLine($"    *output = {nodeVars[data.m_OutputNode.m_Id]};");
            }

            cCode.AppendLine("}");

            // Estimate total time (use enum table)
            foreach (var nodeId in sortedNodes)
            {
                if (!nodes.ContainsKey(nodeId)) continue;
                Node node = nodes[nodeId];
                string type = nodeTypes.ContainsKey(nodeId) ? nodeTypes[nodeId] : "float4";
                string funcName = GetAllNodesFunctionName(node.m_Name, node.m_BlendMode, type);
                if (functionCosts.ContainsKey(funcName))
                    estimatedTotalMs += costValues[functionCosts[funcName]];
                else
                    estimatedTotalMs += costValues[ExecutionCost.VeryHeavy];
            }

            File.WriteAllText(outputPath, cCode.ToString());
        }
    }
    // Add any additional helper methods here
}