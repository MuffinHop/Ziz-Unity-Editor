# EMU Format Version 4: Texture Filename Support

## Overview

EMU format version 4 adds support for embedding texture filenames directly into the .emu file format. This enhancement allows automatic texture loading without requiring manual configuration in emudraw.c file tables.

## Changes Summary

### Unity C# Side (Level.cs)

**New Features:**
- Added `textureFileName` public property for manual texture filename specification
- Automatic texture filename detection from GameObject materials
- EMU format version bumped to 4
- Length-prefixed string encoding for texture filenames

**Usage:**
```csharp
// Manual texture filename specification
level.textureFileName = "my_custom_texture.png";

// Auto-detection from material (default behavior)
// The system automatically extracts texture names from Renderer materials
```

### C Side (emudraw.c)

**New Features:**
- `EMU_VERSION_TEXTURE` constant (value: 4)
- `texture_filename[256]` field in `EmuScene` structure  
- `emu_get_texture_filename()` function to retrieve embedded texture filename
- Enhanced `emu_load_texture_for_file()` with priority system:
  1. Texture filename from EMU file (highest priority)
  2. Texture filename from file table (fallback)

**New Functions:**
```c
const char* emu_get_texture_filename(void);
```

## EMU Version 4 File Format

```
Header (12 bytes):
  magic: uint32_t      // 0x454D5520 ("EMU ")
  version: uint32_t    // 4 (for texture filename support)
  endian: uint32_t     // 0x01020304 (little-endian)

Counts (12 bytes):
  vcount: uint32_t     // Vertex count
  fcount: uint32_t     // Face count  
  lcount: uint32_t     // Leaf count

NEW: Texture Filename (variable length):
  length: uint32_t     // Length of texture filename string
  filename: char[]     // UTF-8 encoded filename string (no null terminator in file)

Vertex Data:
  vertices: float[vcount * 3]     // Vertex positions
  normals: float[vcount * 3]      // Vertex normals
  uvs: uint8_t[vcount * 2]        // Texture coordinates (byte format)
  colors: uint8_t[vcount * 3]     // Vertex colors (RGB bytes)

Face Data:
  faces: uint32_t[fcount * 3]     // Triangle indices

Leaf Data:
  For each leaf:
    nfaces: uint32_t              // Number of faces in leaf
    face_indices: uint32_t[]      // Indices into face array  
    bbox_min: float[3]            // Bounding box minimum
    bbox_max: float[3]            // Bounding box maximum

PVS Data:
  pvs_bytes: uint32_t             // Bytes per PVS bitfield
  pvs_data: uint8_t[lcount * pvs_bytes]  // PVS visibility data
```

## Backward Compatibility

EMU version 4 files are **not** backward compatible with older emudraw.c versions. However, the enhanced emudraw.c still supports:
- EMU version 2 (original format)
- EMU version 3 (glb2emu format)  
- EMU version 4 (texture filename format)

## Migration Guide

### For Unity Users

1. **No changes required** - existing Level components will automatically use version 4
2. **Optional**: Set `textureFileName` property for custom texture paths
3. **Auto-detection**: System automatically extracts texture names from materials
4. **Self-contained**: EMU files now carry their texture dependencies

### For C Developers

1. **Update emudraw.c** - Use the enhanced version with texture filename support
2. **Simplified workflow**: EMU files now specify their own textures - no more manual `emu_file_table` configuration needed for texture filenames!
3. **Backward compatibility**: File table-based texture loading still works as fallback for older EMU versions
4. **New API**: Call `emu_get_texture_filename()` and `emu_has_embedded_texture()` to work with embedded textures

### Texture Loading Priority (NEW!)

The enhanced emudraw.c now uses this priority system:

1. **Highest Priority**: Texture filename embedded in EMU file (version 4)
   - Uses reasonable defaults: Linear filtering, Alpha enabled
   - Source: EMU file itself
   
2. **Fallback**: Texture filename from `emu_file_table` configuration
   - Uses configured filter and alpha settings
   - Source: Manual configuration in emudraw.c

This means you can now load EMU files without any `emu_file_table` configuration - the texture information comes directly from the EMU file!

## Example Usage

### Unity Export
```csharp
// Create Level component
Level level = gameObject.AddComponent<Level>();

// Option 1: Let system auto-detect texture from material
// (No additional code needed)

// Option 2: Manually specify texture filename  
level.textureFileName = "assets/my_level_texture.png";

// Export (texture filename embedded in .emu file)
level.ExportLevel();
```

### C Loading
```c
// Load EMU file (automatically reads texture filename if version 4)
if (load_emu("level.emu")) {
    // Get embedded texture filename
    const char* texture_name = emu_get_texture_filename();
    if (texture_name) {
        printf("EMU file specifies texture: %s\n", texture_name);
        // Texture is automatically loaded via enhanced emu_load_texture_for_file()
    }
}
```

## Testing

Use the provided test program:
```bash
# Compile test program
gcc -o test_emu_texture_filename test_emu_texture_filename.c

# Create test EMU file with custom texture filename
./test_emu_texture_filename my_texture.png

# Test file: test_texture_filename.emu will be created
```

## Debug Output

With `EMU_DEBUG_LEVEL >= 3`, you'll see:
```
EMU: Texture filename from EMU file: assets/my_texture.png
Loaded texture: assets/my_texture.png (Manager ID: 5) [Source: EMU file]
```

## Benefits

1. **Simplified Workflow**: No manual texture configuration in emudraw.c file tables
2. **Self-Contained Files**: EMU files carry their texture dependencies
3. **Auto-Detection**: Unity materials automatically provide texture filenames
4. **Fallback Support**: Existing file table system still works
5. **Future-Proof**: Foundation for additional metadata in EMU files
