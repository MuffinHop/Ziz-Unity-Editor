#include "rat_handler.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "utils.h" // For file_read_into_buffer

#define RAT3_MAGIC 0x33544152
#define RATM_MAGIC 0x4D544152

// --- Private Helper Prototypes ---
static RatAnimation* load_rat_animation(const char* path);
static RatMeshData* load_rat_mesh_data(const char* path, const char* base_dir);
static void decompress_frame(RatModel* model, uint32_t frame_index);
static uint32_t get_bits(uint32_t* data, uint64_t* bit_offset, uint8_t num_bits);
static int32_t sign_extend(uint32_t value, uint8_t num_bits);


// --- Public API Implementation ---

RatModel* rat_model_create_from_chunk(const char* rat_chunk_path, int texture_id) {
    printf("Creating RatModel from chunk: %s\n", rat_chunk_path);

    RatModel* model = (RatModel*)calloc(1, sizeof(RatModel));
    if (!model) {
        fprintf(stderr, "Failed to allocate memory for RatModel.\n");
        return NULL;
    }

    model->animation = load_rat_animation(rat_chunk_path);
    if (!model->animation) {
        fprintf(stderr, "Failed to load RAT animation from %s.\n", rat_chunk_path);
        rat_model_destroy(model);
        return NULL;
    }

    // Construct path for .ratmesh file
    char base_dir[256];
    strncpy(base_dir, rat_chunk_path, sizeof(base_dir));
    char* last_slash = strrchr(base_dir, '/');
    if (last_slash) {
        *(last_slash + 1) = '\0';
    } else {
        base_dir[0] = '\0'; // It's in the current directory
    }

    model->mesh_data = load_rat_mesh_data(model->animation->mesh_data_filename, base_dir);
    if (!model->mesh_data) {
        fprintf(stderr, "Failed to load RAT mesh data from %s (referenced by %s).\n", model->animation->mesh_data_filename, rat_chunk_path);
        rat_model_destroy(model);
        return NULL;
    }
    
    // Allocate buffers for runtime decompression
    model->decompressed_vertices_u8 = (VertexU8*)malloc(sizeof(VertexU8) * model->animation->num_vertices);
    model->current_frame_vertices = (Vector3*)malloc(sizeof(Vector3) * model->animation->num_vertices);
    if (!model->decompressed_vertices_u8 || !model->current_frame_vertices) {
        fprintf(stderr, "Failed to allocate memory for vertex buffers.\n");
        rat_model_destroy(model);
        return NULL;
    }

    model->texture_id = texture_id;
    model->current_frame = -1; // Force initial decompression
    model->is_valid = true;

    printf("Successfully created RatModel for %s.\n", rat_chunk_path);
    
    // Decompress the first frame to have a valid initial state
    rat_model_update(model, 0);

    return model;
}

void rat_model_destroy(RatModel* model) {
    if (!model) return;

    if (model->animation) {
        free(model->animation->first_frame_quantized);
        free(model->animation->first_frame_raw);
        free(model->animation->bit_widths_x);
        free(model->animation->bit_widths_y);
        free(model->animation->bit_widths_z);
        free(model->animation->delta_stream);
        free(model->animation);
    }

    if (model->mesh_data) {
        free(model->mesh_data->uvs);
        free(model->mesh_data->colors);
        free(model->mesh_data->indices);
        free(model->mesh_data);
    }

    free(model->decompressed_vertices_u8);
    free(model->current_frame_vertices);
    
    // TODO: Free GL buffers if they were created
    // glDeleteBuffers(1, &model->vbo_vertices);
    // ...

    free(model);
}

void rat_model_update(RatModel* model, uint32_t local_frame) {
    if (!model || !model->is_valid || local_frame == model->current_frame) {
        return;
    }
    
    uint32_t frame_count = rat_model_get_chunk_frame_count(model);
    if (local_frame >= frame_count) {
        // This can happen, just clamp to the last frame
        local_frame = frame_count > 0 ? frame_count - 1 : 0;
    }

    decompress_frame(model, local_frame);
    model->current_frame = local_frame;

    // TODO: Update VBOs if using them
    // glBindBuffer(GL_ARRAY_BUFFER, model->vbo_vertices);
    // glBufferSubData(GL_ARRAY_BUFFER, 0, sizeof(Vector3) * model->animation->num_vertices, model->current_frame_vertices);
}

void rat_model_render(RatModel* model) {
    if (!model || !model->is_valid) {
        return;
    }
    // This is a placeholder for actual rendering logic.
    // In a real engine, you would bind shaders, set uniforms,
    // bind VBOs, and call glDrawElements.
    
    // Example of what it might look like:
    /*
    glUseProgram(my_shader);
    glBindTexture(GL_TEXTURE_2D, model->texture_id);
    
    // Bind vertex data
    glBindBuffer(GL_ARRAY_BUFFER, model->vbo_vertices);
    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 0, NULL);
    glEnableVertexAttribArray(0);

    // Bind UVs
    glBindBuffer(GL_ARRAY_BUFFER, model->vbo_uvs);
    glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 0, NULL);
    glEnableVertexAttribArray(1);

    // Bind colors
    glBindBuffer(GL_ARRAY_BUFFER, model->vbo_colors);
    glVertexAttribPointer(2, 4, GL_FLOAT, GL_FALSE, 0, NULL);
    glEnableVertexAttribArray(2);

    // Bind indices and draw
    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, model->ebo_indices);
    glDrawElements(GL_TRIANGLES, model->mesh_data->num_indices, GL_UNSIGNED_SHORT, NULL);
    */
}

bool rat_model_is_valid(RatModel* model) {
    return model && model->is_valid;
}

uint32_t rat_model_get_chunk_frame_count(RatModel* model) {
    if (!model || !model->animation || model->animation->num_vertices == 0) {
        return 0;
    }
    // The delta stream contains data for (num_frames - 1) frames.
    // The total number of frames in the chunk is therefore num_frames.
    return model->animation->num_frames;
}


// --- Private Helper Functions ---

static RatAnimation* load_rat_animation(const char* path) {
    size_t buffer_size;
    uint8_t* buffer = file_read_into_buffer(path, &buffer_size);
    if (!buffer) {
        return NULL;
    }

    if (buffer_size < sizeof(RatHeader)) {
        fprintf(stderr, "File %s is smaller than RatHeader.\n", path);
        free(buffer);
        return NULL;
    }

    RatHeader* header = (RatHeader*)buffer;
    if (header->magic != RAT3_MAGIC) {
        fprintf(stderr, "Invalid RAT3 magic number in %s.\n", path);
        free(buffer);
        return NULL;
    }

    RatAnimation* anim = (RatAnimation*)calloc(1, sizeof(RatAnimation));
    if (!anim) {
        free(buffer);
        return NULL;
    }

    anim->num_vertices = header->num_vertices;
    anim->num_frames = header->num_frames;
    anim->min_x = header->min_x;
    anim->min_y = header->min_y;
    anim->min_z = header->min_z;
    anim->max_x = header->max_x;
    anim->max_y = header->max_y;
    anim->max_z = header->max_z;
    anim->is_first_frame_raw = header->is_first_frame_raw;

    // Copy mesh data filename
    strncpy(anim->mesh_data_filename, (char*)(buffer + header->mesh_data_filename_offset), header->mesh_data_filename_length);
    anim->mesh_data_filename[header->mesh_data_filename_length] = '\0';

    // Load first frame data
    if (anim->is_first_frame_raw) {
        size_t size = sizeof(Vector3) * anim->num_vertices;
        anim->first_frame_raw = (Vector3*)malloc(size);
        memcpy(anim->first_frame_raw, buffer + header->raw_first_frame_offset, size);
    } else {
        size_t size = sizeof(VertexU8) * anim->num_vertices;
        anim->first_frame_quantized = (VertexU8*)malloc(size);
        // The quantized first frame is assumed to be right after the header
        memcpy(anim->first_frame_quantized, buffer + sizeof(RatHeader), size);
    }

    // Load bit widths
    size_t bw_size = sizeof(uint8_t) * anim->num_vertices;
    uint8_t* bw_data = buffer + header->bit_widths_offset;
    anim->bit_widths_x = (uint8_t*)malloc(bw_size);
    anim->bit_widths_y = (uint8_t*)malloc(bw_size);
    anim->bit_widths_z = (uint8_t*)malloc(bw_size);
    memcpy(anim->bit_widths_x, bw_data, bw_size);
    memcpy(anim->bit_widths_y, bw_data + bw_size, bw_size);
    memcpy(anim->bit_widths_z, bw_data + 2 * bw_size, bw_size);

    // Load delta stream
    uint32_t delta_stream_byte_size = buffer_size - header->delta_offset;
    anim->delta_stream_word_count = delta_stream_byte_size / 4;
    anim->delta_stream = (uint32_t*)malloc(delta_stream_byte_size);
    memcpy(anim->delta_stream, buffer + header->delta_offset, delta_stream_byte_size);

    free(buffer);
    return anim;
}

static RatMeshData* load_rat_mesh_data(const char* filename, const char* base_dir) {
    char full_path[512];
    snprintf(full_path, sizeof(full_path), "%s%s", base_dir, filename);
    
    size_t buffer_size;
    uint8_t* buffer = file_read_into_buffer(full_path, &buffer_size);
    if (!buffer) {
        return NULL;
    }

    if (buffer_size < sizeof(RatMeshHeader)) {
        fprintf(stderr, "File %s is smaller than RatMeshHeader.\n", full_path);
        free(buffer);
        return NULL;
    }

    RatMeshHeader* header = (RatMeshHeader*)buffer;
    if (header->magic != RATM_MAGIC) {
        fprintf(stderr, "Invalid RATM magic number in %s.\n", full_path);
        free(buffer);
        return NULL;
    }

    RatMeshData* mesh = (RatMeshData*)calloc(1, sizeof(RatMeshData));
    if (!mesh) {
        free(buffer);
        return NULL;
    }

    mesh->num_vertices = header->num_vertices;
    mesh->num_indices = header->num_indices;

    // Load UVs
    size_t uv_size = sizeof(VertexUV) * mesh->num_vertices;
    mesh->uvs = (VertexUV*)malloc(uv_size);
    memcpy(mesh->uvs, buffer + header->uv_offset, uv_size);

    // Load Colors
    size_t color_size = sizeof(VertexColor) * mesh->num_vertices;
    mesh->colors = (VertexColor*)malloc(color_size);
    memcpy(mesh->colors, buffer + header->color_offset, color_size);

    // Load Indices
    size_t indices_size = sizeof(uint16_t) * mesh->num_indices;
    mesh->indices = (uint16_t*)malloc(indices_size);
    memcpy(mesh->indices, buffer + header->indices_offset, indices_size);
    
    // Copy texture filename
    strncpy(mesh->texture_filename, (char*)(buffer + header->texture_filename_offset), header->texture_filename_length);
    mesh->texture_filename[header->texture_filename_length] = '\0';

    free(buffer);
    return mesh;
}

static void decompress_frame(RatModel* model, uint32_t frame_index) {
    RatAnimation* anim = model->animation;
    VertexU8* prev_frame = model->decompressed_vertices_u8;

    if (frame_index == 0) {
        if (anim->is_first_frame_raw) {
            // De-quantize the raw float frame for future delta application
            for (uint32_t i = 0; i < anim->num_vertices; ++i) {
                prev_frame[i].x = (uint8_t)(255.0f * (anim->first_frame_raw[i].x - anim->min_x) / (anim->max_x - anim->min_x));
                prev_frame[i].y = (uint8_t)(255.0f * (anim->first_frame_raw[i].y - anim->min_y) / (anim->max_y - anim->min_y));
                prev_frame[i].z = (uint8_t)(255.0f * (anim->first_frame_raw[i].z - anim->min_z) / (anim->max_z - anim->min_z));
            }
        } else {
            // First frame is already quantized, just copy it
            memcpy(prev_frame, anim->first_frame_quantized, sizeof(VertexU8) * anim->num_vertices);
        }
    } else {
        // This is where the delta decompression happens.
        // We need to reconstruct frame `frame_index` by applying deltas from frame 0 up to it.
        // For simplicity and to avoid storing all frames, we re-decompress from frame 0 each time.
        // A more optimized version would cache the last decompressed frame.
        
        // Start with frame 0
        if (anim->is_first_frame_raw) {
             for (uint32_t i = 0; i < anim->num_vertices; ++i) {
                prev_frame[i].x = (uint8_t)(255.0f * (anim->first_frame_raw[i].x - anim->min_x) / (anim->max_x - anim->min_x));
                prev_frame[i].y = (uint8_t)(255.0f * (anim->first_frame_raw[i].y - anim->min_y) / (anim->max_y - anim->min_y));
                prev_frame[i].z = (uint8_t)(255.0f * (anim->first_frame_raw[i].z - anim->min_z) / (anim->max_z - anim->min_z));
            }
        } else {
            memcpy(prev_frame, anim->first_frame_quantized, sizeof(VertexU8) * anim->num_vertices);
        }

        uint64_t bit_offset = 0;
        for (uint32_t f = 1; f <= frame_index; ++f) {
            for (uint32_t i = 0; i < anim->num_vertices; ++i) {
                int32_t dx = sign_extend(get_bits(anim->delta_stream, &bit_offset, anim->bit_widths_x[i]), anim->bit_widths_x[i]);
                int32_t dy = sign_extend(get_bits(anim->delta_stream, &bit_offset, anim->bit_widths_y[i]), anim->bit_widths_y[i]);
                int32_t dz = sign_extend(get_bits(anim->delta_stream, &bit_offset, anim->bit_widths_z[i]), anim->bit_widths_z[i]);

                prev_frame[i].x += dx;
                prev_frame[i].y += dy;
                prev_frame[i].z += dz;
            }
        }
    }

    // Now de-quantize the final `prev_frame` (which is now the current frame) into `current_frame_vertices`
    float range_x = anim->max_x - anim->min_x;
    float range_y = anim->max_y - anim->min_y;
    float range_z = anim->max_z - anim->min_z;

    for (uint32_t i = 0; i < anim->num_vertices; ++i) {
        model->current_frame_vertices[i].x = anim->min_x + (prev_frame[i].x / 255.0f) * range_x;
        model->current_frame_vertices[i].y = anim->min_y + (prev_frame[i].y / 255.0f) * range_y;
        model->current_frame_vertices[i].z = anim->min_z + (prev_frame[i].z / 255.0f) * range_z;
    }
}

// --- Bitstream Helpers (from C# code) ---

static uint32_t get_bits(uint32_t* data, uint64_t* bit_offset, uint8_t num_bits) {
    if (num_bits == 0) return 0;

    uint64_t start_offset = *bit_offset;
    uint32_t word_index = start_offset / 32;
    uint32_t bit_index = start_offset % 32;

    uint32_t value;
    if (bit_index + num_bits <= 32) {
        value = (data[word_index] >> bit_index) & ((1U << num_bits) - 1);
    } else {
        uint32_t part1 = (data[word_index] >> bit_index);
        uint8_t part1_len = 32 - bit_index;
        uint8_t part2_len = num_bits - part1_len;
        uint32_t part2 = data[word_index + 1] & ((1U << part2_len) - 1);
        value = part1 | (part2 << part1_len);
    }

    *bit_offset += num_bits;
    return value;
}

static int32_t sign_extend(uint32_t value, uint8_t num_bits) {
    if (num_bits == 0) return 0;
    uint32_t sign_bit = 1U << (num_bits - 1);
    if ((value & sign_bit) != 0) {
        return (int32_t)(value | (~0U << num_bits));
    }
    return (int32_t)value;
}
