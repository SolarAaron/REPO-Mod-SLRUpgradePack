#!/bin/bash
set -euo pipefail

# icon & texture
inkscape -w 256 -h 256 icon.svg -o skel/icon.png
inkscape -w 512 -h 512 "../unity/My project/Assets/REPO/Mods/upgrade_overcharge.svg" -o "../unity/My project/Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Overcharge_Albedo.png"
inkscape -w 512 -h 512 "../unity/My project/Assets/REPO/Mods/upgrade_armor.svg" -o "../unity/My project/Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Armor_Albedo.png"
inkscape -w 512 -h 512 "../unity/My project/Assets/REPO/Mods/upgrade_heart_of_gold.svg" -o "../unity/My project/Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Heart_Of_Gold_Albedo.png"
inkscape -w 512 -h 512 "../unity/My project/Assets/REPO/Mods/upgrade_regeneration.svg" -o "../unity/My project/Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Regeneration_Albedo.png"
inkscape -w 512 -h 512 "../unity/My project/Assets/REPO/Mods/upgrade_extra_life.svg" -o "../unity/My project/Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Extra_Life_Albedo.png"

# Stage only the DLL from the current build. The staging directory is ignored
# by git, so remove stale DLLs explicitly before creating the package.
mkdir -p skel/plugins
rm -f skel/plugins/*.dll
cp ../bin/Release/netstandard2.1/SLRUpgradePack.dll skel/plugins/SLRUpgradePack.dll

#remove previous package
if [ -f SLRUpgradePack.zip ] ; then
  rm -vf SLRUpgradePack.zip
fi

#package skeleton
pushd skel
zip -r9 ../SLRUpgradePack.zip *
popd
