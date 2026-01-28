# SDKGeneratorBNM

An SDK generator for BNM (ByNameModding) that creates C++ headers from Unity IL2CPP assemblies.

# How to use

1. Compile SDKGeneratorBNM (or use release version)
2. Get your DummyDll from using [Il2CppDumper](https://github.com/Perfare/Il2CppDumper/)
3. Drag Assembly-CSharp.dll (from DummyDll folder) to SDKGeneratorBNM.exe
4. Wait for your SDK to be generated
5. Copy the SDK folder to your project
6. Copy BNM headers to your project (And include it to your Android.mk or CMakeList.txt)
7. Download [BNMIncludes.hpp](https://github.com/official-notfishvr/BNMGameStructureGenerator/blob/main/Include/BNMIncludes.hpp) and place it in your `Include` folder
8. If using the `-b` or `--bnm-resolve` flag, also download [BNMResolve.hpp](https://github.com/Livku2/BNMResolve/blob/main/BNMResolve.hpp) and place it in your `Include` folder

## CMake Configuration

Add the following to your `CMakeLists.txt`:

```cmake
# Add BNM include directory
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/extern/BNM-Android/include)

# Add BNMIncludes.hpp (required)
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/Include)

# Add BNMResolve.hpp (optional, only if you generated SDK with -b flag)
# Uncomment the line below if you used the -b or --bnm-resolve flag
# include_directories(${CMAKE_CURRENT_SOURCE_DIR}/Include)
```

Or for Android.mk:

```makefile
# Add BNM include directory
LOCAL_C_INCLUDES += $(LOCAL_PATH)/extern/BNM-Android/include

# Add BNMIncludes.hpp (required)
LOCAL_C_INCLUDES += $(LOCAL_PATH)/Include

# Add BNMResolve.hpp (optional, only if you generated SDK with -b flag)
# LOCAL_C_INCLUDES += $(LOCAL_PATH)/Include
```

# How to use the SDK

Please read how to Initialize BNM Functions first before using any of the SDK/BNM Functions.

**Example:**
```cpp
#include "SDK/GlobalNamespace/PlayerController.hpp"
#include "SDK/GlobalNamespace/GameManager.hpp"

using namespace GlobalNamespace;
using namespace BNM::UnityEngine;

void ModifyPlayer() {
    auto gameManager = GameManager::get_Instance();
    if (gameManager) {
        auto player = gameManager->GetCurrentPlayer();
        
        if(player) {
            auto transform = ((Component *)player)->get_transform();
            auto position = transform->get_position();
            LOGD("Player Position: %f %f %f", position.x, position.y, position.z);
            
            player->SetHealth(9999.0f);
        }
    }
}
```

# Options

- Use `-s` or `--single-file` to generate everything in one file
- Use `-g` or `--getter-setter` to use getter_setter naming style instead of GetSet
- Use `-a` or `--accessor` to use accessor style (field()->Get(), method()->Call())
- Use `-b` or `--bnm-resolve` to use BNMResolve types (GameObject, Transform, Camera, etc.)
- Use `-h` or `--help` to display help message

## Type Mapping Modes

### Default Mode (without -b flag)
Uses generic Il2CppObject pointers for all BNMResolve types:
- `UnityEngine.GameObject` → `BNM::IL2CPP::Il2CppObject*`
- `UnityEngine.Transform` → `BNM::IL2CPP::Il2CppObject*`
- `UnityEngine.Camera` → `BNM::IL2CPP::Il2CppObject*`
- etc.

**Does not require:** BNMResolve.hpp include

### BNM-Resolve Mode (-b flag)
Uses typed BNMResolve objects for better type safety:
- `UnityEngine.GameObject` → `GameObject*`
- `UnityEngine.Transform` → `Transform*`
- `UnityEngine.Camera` → `Camera*`
- etc.

**Requires:** `BNMResolve.hpp` in your CMake/Android.mk