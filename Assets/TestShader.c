#include "AllNodes.c"
#include <stdlib.h>

// Generated C code from Shader Graph
void ShaderMain(float4* output /* add inputs as needed */) {
    float4 var0;
    Unity_SceneColor(NULL, &var0);
    float4 var1;
    Unity_Add_float4(&var0, NULL, &var1);
    float4 var2;
    Unity_Checkerboard_float4(NULL, NULL, NULL, NULL, &var2);
    *output = var1;
}
