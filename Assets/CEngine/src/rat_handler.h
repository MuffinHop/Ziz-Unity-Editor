#ifndef RAT_HANDLER_H
#define RAT_HANDLER_H

#include <stdint.h>
#include <stdbool.h>

// Forward declarations for GL types to avoid including GL headers here
#ifdef NO_GL_HEADERS
typedef unsigned int GLuint;
#endif

// --- File Format Structures (matching Rat.cs) ---

#pragma pack(push, 1)
// Header for the main animation data file (.rat)
typedef struct {
    uint32_t magic;              // "RAT3" = 0x33544152
    uint32_t num_vertices;
    uint32_t num_frames;
    uint32_t num_indices;
    uint32_t delta_offset;
    uint32_t bit_widths_offset;
    uint32_t mesh_data_filename_offset;
    uint32_t mesh_data_filename_length;
    float min_x, min_y, min_z;
    float max_x, max_y, max_z;
    uint8_t is_first_frame_raw; // 0 = false, 1 = true
    uint8_t reserved[3];
    uint32_t raw_first_frame_offset;
} RatHeader;

// Header for the static mesh data file (.ratmesh)
typedef struct {
    uint32_t magic;              // "RATM" = 0x4D544152
    uint32_t num_vertices;
    uint32_t num_indices;
    uint32_t uv_offset;
    uint32_t color_offset;
    uint32_t indices_offset;
    uint32_t texture_filename_offset;
    uint32_t texture_filename_length;
    uint8_t reserved[16];
} RatMeshHeader;

// Quantized vertex position
typedef struct {
    uint8_t x, y, z;
} VertexU8;

// 3D vector for raw vertex positions
typedef struct {
    float x, y, z;
} Vector3;

// UV coordinates
typedef struct {
    float u, v;
} VertexUV;

// Vertex color
typedef struct {
    float r, g, b, a;
} VertexColor;
#pragma pack(pop)


// --- Runtime Data Structures ---

// Represents the data loaded from a .rat file
typedef struct {
    uint32_t num_vertices;
    uint32_t num_frames; // Total frames in the entire animation, not just this chunk
    float min_x, min_y, min_z;
    float max_x, max_y, max_z;
    
    VertexU8* first_frame_quantized;
    Vector3* first_frame_raw;
    bool is_first_frame_raw;

    uint8_t* bit_widths_x;
    uint8_t* bit_widths_y;
    uint8_t* bit_widths_z;
    
    uint32_t* delta_stream;
    uint32_t delta_stream_word_count;

    char mesh_data_filename[256];
} RatAnimation;

// Represents the data loaded from a .ratmesh file
typedef struct {
    uint32_t num_vertices;
    uint32_t num_indices;
    VertexUV* uvs;
    VertexColor* colors;
    uint16_t* indices;
    char texture_filename[256];
} RatMeshData;

// The main runtime object for a single RAT animation chunk
typedef struct RatModel {
    // --- Core Data ---
    RatAnimation* animation;
    RatMeshData* mesh_data;
    
    // --- Decompression State ---
    VertexU8* decompressed_vertices_u8; // Current frame's quantized vertices
    Vector3* current_frame_vertices;    // Final float vertices for rendering
    uint32_t current_frame;
    
    // --- Rendering ---
    int texture_id;
    bool is_valid;
    
    // --- VBOs (if used) ---
    #ifndef NO_GL_HEADERS
    GLuint vbo_vertices;
    GLuint vbo_uvs;
    GLuint vbo_colors;
    GLuint ebo_indices;
    #endif

} RatModel;


// --- Public API ---

// Functions to be used by rat_actors.c and other systems

/**
 * @brief Creates a RatModel by loading a single .rat chunk file.
 * It also loads the associated .ratmesh file referenced within the .rat file.
 * @param rat_chunk_path Path to the .rat file (e.g., "my_anim_part01of02.rat").
 * @param texture_id The OpenGL texture ID to associate with this model.
 * @return A pointer to the loaded RatModel, or NULL on failure.
 */
RatModel* rat_model_create_from_chunk(const char* rat_chunk_path, int texture_id);

/**
 * @brief Destroys a RatModel and frees all associated memory.
 * @param model The RatModel to destroy.
 */
void rat_model_destroy(RatModel* model);

/**
 * @brief Updates the model to a specific local frame within its chunk.
 * @param model The RatModel to update.
 * @param local_frame The frame index to decompress to.
 */
void rat_model_update(RatModel* model, uint32_t local_frame);

/**
 * @brief Renders the RatModel at its current frame.
 * @param model The RatModel to render.
 */
void rat_model_render(RatModel* model);

/**
 * @brief Checks if a RatModel is valid and ready for use.
 * @param model The RatModel to check.
 * @return True if the model is valid, false otherwise.
 */
bool rat_model_is_valid(RatModel* model);

/**
 * @brief Gets the number of animation frames contained within this specific chunk.
 * This is calculated from the delta stream size.
 * @param model The RatModel (chunk) to query.
 * @return The number of frames in the chunk.
 */
uint32_t rat_model_get_chunk_frame_count(RatModel* model);

// Functions for transform, though these are often handled by the actor system
void rat_model_set_position(RatModel* model, float x, float y, float z);
void rat_model_set_rotation(RatModel* model, float x_rad, float y_rad, float z_rad);
void rat_model_set_scale(RatModel* model, float x, float y, float z);

#endif // RAT_HANDLER_H
