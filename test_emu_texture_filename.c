/*
 * test_emu_texture_filename.c - Test program for EMU version 4 texture filename support
 * 
 * This program tests the new texture filename feature in EMU format version 4
 * by creating a simple test file and loading it with emudraw.c functions.
 */

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>

// Test function to create a minimal EMU version 4 file with texture filename
void create_test_emu_v4_file(const char* filename, const char* texture_filename) {
    FILE* file = fopen(filename, "wb");
    if (!file) {
        printf("Failed to create test file: %s\n", filename);
        return;
    }
    
    printf("Creating test EMU version 4 file: %s\n", filename);
    printf("Texture filename: %s\n", texture_filename);
    
    // EMU header
    uint32_t magic = 0x454D5520;     // "EMU " (space at end)
    uint32_t version = 4;            // Version 4 for texture filename support  
    uint32_t endian = 0x01020304;    // Little-endian marker
    
    fwrite(&magic, 4, 1, file);
    fwrite(&version, 4, 1, file);
    fwrite(&endian, 4, 1, file);
    
    // Counts (minimal test data)
    uint32_t vcount = 3;  // 3 vertices (triangle)
    uint32_t fcount = 1;  // 1 face
    uint32_t lcount = 1;  // 1 leaf
    
    fwrite(&vcount, 4, 1, file);
    fwrite(&fcount, 4, 1, file);
    fwrite(&lcount, 4, 1, file);
    
    // NEW: Texture filename with length prefix
    uint32_t texture_filename_length = strlen(texture_filename);
    fwrite(&texture_filename_length, 4, 1, file);
    fwrite(texture_filename, texture_filename_length, 1, file);
    
    // Minimal vertex data (triangle)
    float vertices[9] = {
        -1.0f, -1.0f, 0.0f,  // Vertex 0
         1.0f, -1.0f, 0.0f,  // Vertex 1
         0.0f,  1.0f, 0.0f   // Vertex 2
    };
    fwrite(vertices, sizeof(float), 9, file);
    
    // Minimal normal data (all pointing up)
    float normals[9] = {
        0.0f, 0.0f, 1.0f,    // Normal 0
        0.0f, 0.0f, 1.0f,    // Normal 1
        0.0f, 0.0f, 1.0f     // Normal 2
    };
    fwrite(normals, sizeof(float), 9, file);
    
    // Minimal UV data (2 bytes per vertex)
    uint8_t uvs[6] = {
        0, 255,     // UV 0 (0.0, 1.0)
        255, 255,   // UV 1 (1.0, 1.0)
        127, 0      // UV 2 (0.5, 0.0)
    };
    fwrite(uvs, 1, 6, file);
    
    // Minimal color data (3 bytes per vertex, RGB)
    uint8_t colors[9] = {
        255, 0, 0,    // Red
        0, 255, 0,    // Green
        0, 0, 255     // Blue
    };
    fwrite(colors, 1, 9, file);
    
    // Minimal face data (1 triangle)
    uint32_t face[3] = { 0, 1, 2 };
    fwrite(face, sizeof(uint32_t), 3, file);
    
    // Minimal leaf data
    uint32_t leaf_nfaces = 1;
    fwrite(&leaf_nfaces, 4, 1, file);
    
    uint32_t leaf_face_index = 0;
    fwrite(&leaf_face_index, 4, 1, file);
    
    // Leaf bounding box (Vec3 min, Vec3 max)
    float bbox[6] = {
        -1.0f, -1.0f, 0.0f,  // min
         1.0f,  1.0f, 0.0f   // max
    };
    fwrite(bbox, sizeof(float), 6, file);
    
    // PVS data
    uint32_t pvs_bytes = 1; // 1 byte for 1 leaf
    fwrite(&pvs_bytes, 4, 1, file);
    
    uint8_t pvs_data = 0xFF; // All leaves visible
    fwrite(&pvs_data, 1, 1, file);
    
    fclose(file);
    
    printf("Test EMU file created successfully!\n");
    printf("File size: %ld bytes\n", ftell(file));
}

int main(int argc, char* argv[]) {
    printf("EMU Version 4 Texture Filename Test\n");
    printf("====================================\n");
    
    const char* test_filename = "test_texture_filename.emu";
    const char* texture_filename = "test_texture.png";
    
    if (argc > 1) {
        texture_filename = argv[1];
    }
    
    // Create test file
    create_test_emu_v4_file(test_filename, texture_filename);
    
    // Test loading (would require linking with emudraw.c)
    printf("\nTo test loading:\n");
    printf("1. Add '%s' to your emu_file_table in emudraw.c\n", test_filename);
    printf("2. Compile and run your main application\n");
    printf("3. Check debug output for texture filename loading\n");
    
    return 0;
}
