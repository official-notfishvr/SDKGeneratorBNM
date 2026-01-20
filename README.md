# SDKGeneratorBNM

An SDK generator for BNM (ByNameModding) that creates C++ headers from Unity IL2CPP assemblies.

# How to use

1. Compile SDKGeneratorBNM (or use release version)
2. Get your DummyDll from using [Il2CppDumper](https://github.com/Perfare/Il2CppDumper/)
3. Drag Assembly-CSharp.dll (from DummyDll folder) to SDKGeneratorBNM.exe
4. Wait for your SDK to be generated
5. Copy the SDK folder to your project
6. Copy BNM headers to your project (And include it to your Android.mk or CMakeList.txt)

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
- Use `-h` or `--help` to display help message