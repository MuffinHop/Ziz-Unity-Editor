# SceneController Usage Guide

The SceneController component manages activation and deactivation of Camera, Actor, and Level components in your Unity scene, recording their states over time and exporting to a binary format for C engine integration.

## Quick Setup

1. **Add SceneController to Scene:**
   - Use menu: `Tools > Create Scene Controller`
   - Or manually add the component to any GameObject

2. **Configure Settings:**
   - `fileName`: Base name for the .scn output file
   - `recordingFPS`: Frame rate for state recording (default: 30)
   - `autoStartRecording`: Start recording automatically on play
   - `scanChildrenForComponents`: Search entire scene vs current GameObject
   - Track options for each component type

## Component Discovery

The SceneController automatically finds and tracks:
- **RecordCamera** components (saves to .cam files)
- **Actor** components (saves to .act files) 
- **Level** components (saves to .emu files)

Each component gets a unique ID and file path mapping.

## Recording Process

1. **Auto-Start:** Recording begins when play mode starts (if enabled)
2. **State Tracking:** Records component enabled/disabled states at target FPS
3. **Change Detection:** Logs when components activate/deactivate
4. **Auto-Save:** Saves .scn file when play mode ends

## Manual Control

```csharp
// Get reference to SceneController
SceneController controller = FindObjectOfType<SceneController>();

// Start/stop recording
controller.StartRecording();
controller.StopRecording();

// Check component state
bool isActive = controller.IsComponentActive("MyCamera");

// Change component state
controller.SetComponentActive("MyActor", false);

// Get all tracked components
string[] components = controller.GetTrackedComponentNames();
```

## File Output

The SceneController generates `.scn` files in the `GeneratedData/` directory with:

- **Binary format** optimized for C engines
- **Bit-packed states** for memory efficiency
- **Component metadata** linking to asset files
- **Time-based keyframes** for interpolation

## C Engine Integration

The .scn file format is designed for direct loading in C engines:

```c
// Example C loading code
SceneHeader header;
fread(&header, sizeof(SceneHeader), 1, file);

SceneComponent* components = malloc(sizeof(SceneComponent) * header.num_components);
fread(components, sizeof(SceneComponent), header.num_components, file);

// Read and decode keyframes...
```

See the detailed C integration documentation in SceneController.cs for complete implementation details.

## Debug Features

- **GUI overlay** shows recording status and statistics
- **Console logging** for state changes and performance metrics
- **State change tracking** with detailed component information
- **File size analysis** and compression statistics

## Best Practices

1. **Single Controller:** Use one SceneController per scene
2. **Stable Names:** Avoid changing GameObject names during recording
3. **Performance:** Higher FPS = more accurate state capture but larger files
4. **Testing:** Use the GUI overlay to monitor recording in real-time

## Integration with Other Components

The SceneController works alongside:
- **RecordCamera**: Tracks camera activation with motion recording
- **Actor**: Tracks actor animation with transform recording  
- **Level**: Tracks level export with BSP/PVS computation

All files are cross-referenced and synchronized for complete scene reproduction in your C engine.
