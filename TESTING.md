# Unity Level Export System - Testing Guide

## Quick Setup for Testing EMU Export:

### 1. Create Test Scene in Unity:

1. **Create a new scene** or use existing scene
2. **Add some 3D objects** (cubes, spheres, etc.)
3. **Add the Level component** to an empty GameObject
4. **Configure Level settings**:
   - Set `outputFileName` to "level.emu"
   - Enable `includeChildren` = true
   - Enable `showDebugInfo` = true for validation output

### 2. Level Component Settings:

**Export Settings:**
- `outputFileName`: "level.emu"
- `includeChildren`: true (to scan all scene objects)

**Debug:**
- `showDebugInfo`: true (enables EMU structure dump)
- `enablePerformanceMonitoring`: true

### 3. Testing the Export:

1. **Enter Play Mode** in Unity
2. **Exit Play Mode** - this automatically triggers the export
3. **Check Console** for export messages:
   - Look for "✅ EMU file format validation PASSED"
   - Check EMU structure dump output
4. **Find the generated file** at: `ProjectRoot/GeneratedData/level.emu`

### 4. Verify EMU File:

The console should show:
```
✅ EMU file format validation PASSED
EMU file written: /path/to/ProjectRoot/GeneratedData/level.emu
Compatible with emudraw.c and glb2emu format
EMU file size: XXXX bytes
Vertices: XXX, Faces: XXX, Leaves: XXX
```

### 5. Use with emudraw.c:

1. **Copy `level.emu`** to your emudraw sourcefiles directory
2. **Update emudraw.c** emu_file_table:
   ```c
   {"sourcefiles/level.emu", "assets/your_texture.png", 1, 0, 0}
   ```
3. **Compile and run** emudraw
4. **Navigate** with WASD + mouse

### Troubleshooting:

**No mesh data found:**
- Ensure objects have MeshFilter + MeshRenderer components
- Enable `includeChildren` to scan child objects
- Check that objects are not disabled

**EMU validation failed:**
- Check Unity Console for specific error messages
- Verify the file was written to GeneratedData folder
- Make sure no other process is using the file

**File not visible in emudraw:**
- Verify EMU format validation passed
- Check file size is > 0 bytes
- Ensure emudraw emu_file_table is configured correctly
- Verify texture file exists in emudraw assets folder

## EMU Format Compatibility:

✅ **Magic Number**: 0x454D5520 ("EMU " with space)  
✅ **Version**: 3 (glb2emu compatible)  
✅ **Endian**: Little-endian (0x01020304)  
✅ **Triangular faces** only (emudraw.c requirement)  
✅ **RGB vertex colors** (byte format)  
✅ **UV coordinates** (float format)  
✅ **BSP tree structure** with PVS data  

The generated EMU files are fully compatible with both glb2emu.c and emudraw.c!
