# About

This is a C# + HLSL Shader sample on how to read back data from RWStructuredBuffer UAV.
Tested on Unity 6000.0.40f1 using the **built-in render pipeline** on Vulkan/OpenGLCore/D3D11.

The script provides comparison between 3 different methods to receive buffer data from a
Shader. Execution times are also provided.

**As the time of writing this, RWStructuredBuffer readback on URP with Vulkan/D3D11/D3D12
does NOT work. This is a bug from the side of Unity.**

## Usage

Just create a new Material with the provided shader, create a cube GameObject, assign the
C# script to it, assign the material to its MeshRenderer, and watch the Console logs.

If you see any errors in the logs => most probably a Unity bug.
