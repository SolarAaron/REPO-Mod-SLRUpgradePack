#!/bin/bash

# icon & texture
inkscape -w 256 -h 256 icon.svg -o skel/icon.png
inkscape -w 512 -h 512 ../Assets/REPO/Mods/upgrade_overcharge.svg -o ../Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Overcharge_Albedo.png
inkscape -w 512 -h 512 ../Assets/REPO/Mods/upgrade_armor.svg -o ../Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Armor_Albedo.png
inkscape -w 512 -h 512 ../Assets/REPO/Mods/upgrade_heart_of_gold.svg -o ../Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Heart_Of_Gold_Albedo.png
inkscape -w 512 -h 512 ../Assets/REPO/Mods/upgrade_regeneration.svg -o ../Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Regeneration_Albedo.png
inkscape -w 512 -h 512 ../Assets/REPO/Mods/upgrade_extra_life.svg -o ../Assets/REPO/Mods/Textures/Texture2Ds/Upgrade_Extra_Life_Albedo.png

#dll
cp ../bin/release/netstandard2.1/SLRUpgradePack.dll skel/plugins

#remove previous package
if [ -f SLRUpgradePack.zip ] ; then
  rm -vf SLRUpgradePack.zip
fi

#package skeleton
pushd skel
zip -r9 ../SLRUpgradePack.zip *
popd
