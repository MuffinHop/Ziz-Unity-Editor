/*
 * Example: Using EMU Version 4 with Embedded Texture Filenames
 * 
 * This example shows how the new texture filename feature simplifies EMU loading
 */

// OLD WAY (EMU version 2): Required manual emu_file_table configuration
static EmuFileEntry emu_file_table[MAX_EMU_FILES] = {
    {"sourcefiles/level.emu", "assets/level_texture.png", 1, 0, 0},  // Manual texture config
    // ... more entries needed for each EMU file
};

// NEW WAY (EMU version 4): No manual configuration needed!
// Just load the EMU file and the texture filename comes from the file itself

int main() {
    // Load EMU file (version 4 automatically includes texture filename)
    if (load_emu("level.emu")) {
        
        // Check if EMU has embedded texture filename
        if (emu_has_embedded_texture()) {
            printf("✅ EMU file specifies its own texture: %s\n", emu_get_texture_filename());
            printf("✅ No manual emu_file_table configuration needed!\n");
        } else {
            printf("⚠️  EMU file doesn't specify texture, using emu_file_table fallback\n");
        }
        
        // Texture is automatically loaded with reasonable defaults:
        // - Linear filtering (better quality)
        // - Alpha blending enabled (transparency support)
        
        // Render the scene - texture is already bound and ready
        render_scene(0, 50000);
    }
    
    return 0;
}

/*
 * Console output for EMU version 4:
 * 
 * EMU: Texture filename from EMU file: assets/my_level_texture.png
 * EMU: Using texture filename from EMU file: assets/my_level_texture.png
 * Loaded texture: assets/my_level_texture.png (Manager ID: 1) [Source: EMU file]
 * EMU: Using default settings for EMU file texture: Linear filter, Alpha enabled
 * ✅ EMU file specifies its own texture: assets/my_level_texture.png
 * ✅ No manual emu_file_table configuration needed!
 */

/*
 * Benefits of EMU Version 4:
 * 
 * 1. SELF-CONTAINED: EMU files carry their texture dependencies
 * 2. NO CONFIGURATION: No need to manually configure emu_file_table for textures
 * 3. AUTO-DETECTION: Unity automatically extracts texture names from materials
 * 4. REASONABLE DEFAULTS: Linear filtering + alpha for better quality
 * 5. BACKWARD COMPATIBLE: Still works with older EMU files via fallback
 */
